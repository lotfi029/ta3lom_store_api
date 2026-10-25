# 02: Store API ERD (r5 donations MVP)

**Schema:** `store` · PostgreSQL 18 · EF Core 10 (snake_case). **The source of truth is the EF model plus one fresh `InitialSchema` migration.** No reference DDL is kept in the repo.

**Conventions:**

- **Primary keys:** `uuid` generated as `Guid.CreateVersion7()`; `bigint identity` for ledger tables.
- **Enums:** `text` + `CHECK`.
- **Bilingual text:** `*_ar NOT NULL`, `*_en NULL`.
- **Money:** `numeric(12,2)`.
- **Time:** `timestamptz` UTC.
- **Optimistic concurrency:** `xmin`.

**Business rules:** D-xx refer to [01-api-business-requirements.md](01-api-business-requirements.md).

**Built in:** R5-Core = **19 tables**, 1 sequence, 1 view. R5-Payments (next session) adds 7 tables (§3).

---

## 1. R5-Core: overview

```mermaid
erDiagram
    category ||--o{ project : contains
    media_asset ||--o{ category : "image"
    media_asset ||--o{ project : "cover"
    project ||--o{ project_option : "fixed-price options"
    project ||--o{ project_slug_history : "old slugs"

    customer ||--o{ cart : owns
    cart ||--o{ cart_line : has
    project |o--o{ cart_line : "target = project"
    category |o--o{ cart_line : "target = category"
    project_option |o--o{ cart_line : "chosen option"

    customer ||--o{ orders : places
    orders ||--o{ order_line : has
    project |o--o{ order_line : "snapshot target"
    category |o--o{ order_line : "snapshot target"
    project_option |o--o{ order_line : "snapshot option"

    customer ||--o{ refresh_token : "subject=customer"
    staff_user ||--o{ refresh_token : "subject=staff"
    customer ||..o{ otp_challenge : "by mobile"
```

Platform tables with no FK into the graph: `setting`, `audit_log`, `idempotency_key`, `outbox_message`, `message_template`, `notification`.

---

## 2. Tables

### 2.1 Catalogue

```mermaid
erDiagram
    category {
        uuid id PK
        text slug UK "^[a-z0-9-]{2,60}$"
        text name_ar
        text name_en
        text description_ar
        text description_en
        uuid image_asset_id FK "nullable"
        bool accepts_general_gifts "default true (BR-A05)"
        int sort
        bool is_active
        timestamptz created_at
        timestamptz updated_at
        xid xmin
    }
    project {
        uuid id PK
        uuid category_id FK "NOT NULL, RESTRICT"
        text slug UK "^[a-z0-9-]{2,80}$ (D-08)"
        text status "draft | published | archived (D-07)"
        text title_ar "2-160"
        text title_en
        text summary_ar "<= 300"
        text summary_en
        text body_ar "Markdown"
        text body_en
        uuid cover_asset_id FK "image only (D-06)"
        bool allow_open_amount "default true (BR-A04)"
        numeric min_amount "nullable, > 0; NULL = setting"
        text badge "new | urgent | NULL"
        int sort
        text seo_title_ar
        text seo_title_en
        text seo_desc_ar
        text seo_desc_en
        timestamptz published_at
        timestamptz created_at
        timestamptz updated_at
        tsvector search_vector "generated: ar_normalize(title, summary)"
        xid xmin
    }
    project_option {
        uuid id PK
        uuid project_id FK "CASCADE"
        text label_ar "2-60"
        text label_en
        numeric amount "> 0"
        int sort
        bool is_active
    }
    project_slug_history {
        bigint id PK
        uuid project_id FK "CASCADE"
        text old_slug UK
        timestamptz changed_at
    }
    media_asset {
        uuid id PK
        text kind "image | pdf"
        text storage_key UK "server-generated"
        bool is_public
        text mime
        bigint size_bytes "> 0"
        int width
        int height
        text alt_ar "required for image"
        text alt_en
        timestamptz created_at
        text created_by
    }
```

**Indexes and constraints:**

- **Project:**
  - `ix_project_listing (status, category_id, sort)`;
  - GIN `search_vector`, plus trigram on `ar_normalize(title_ar)`;
  - `ck_project_min (min_amount IS NULL OR min_amount > 0)`.
- **Project option:** `ux_option_label (project_id, label_ar)`.
- **Category:** `ix_category_sort (is_active, sort)`.
- **Media:**
  - `ck_media_alt (kind <> 'image' OR alt_ar IS NOT NULL)`;
  - an image used as a `cover_asset_id` or `image_asset_id` must have `kind = image` (checked in code, D-06).

### 2.2 Identity

```mermaid
erDiagram
    customer {
        uuid id PK
        text mobile UK "+9665XXXXXXXX"
        text full_name
        citext email
        char locale "ar | en"
        bool marketing_opt_in
        text status "active | blocked | anonymised (D-13)"
        timestamptz pdpl_consent_at "set on first sign-in (D-14)"
        timestamptz last_login_at
        timestamptz created_at
        timestamptz updated_at
        xid xmin
    }
    otp_challenge {
        uuid id PK
        text mobile
        text purpose "login"
        char code_hash "HMAC-SHA256 + pepper"
        smallint attempts "<= 5"
        timestamptz expires_at "+5 min"
        timestamptz consumed_at
        char ip_hash
        timestamptz created_at
    }
    staff_user {
        uuid id PK
        citext email UK
        text name
        text pass_hash "PBKDF2-SHA512 600k (D-12)"
        bool must_change_password "true after create/reset"
        timestamptz password_changed_at
        bytea totp_secret_enc "Data Protection"
        bigint totp_last_step "replay guard"
        text_array roles "admin | store_manager | finance | viewer"
        int failed_count
        timestamptz first_failed_at "15-min window"
        timestamptz locked_until
        bool is_active
        timestamptz last_login_at
        timestamptz created_at
        xid xmin
    }
    refresh_token {
        uuid id PK
        text subject_type "customer | staff"
        uuid subject_id "no FK (polymorphic)"
        char token_hash UK "SHA-256"
        uuid family_id
        timestamptz family_expires_at "absolute cap (D-11)"
        timestamptz expires_at "<= family_expires_at"
        timestamptz revoked_at
        uuid replaced_by
        text user_agent
        timestamptz created_at
    }
```

**Indexes:**

- `ix_otp_mobile_recent (mobile, created_at DESC)`;
- `ix_otp_ip_recent (ip_hash, created_at DESC)`;
- `ix_refresh_subject (subject_type, subject_id)`;
- `ix_refresh_family (family_id)`.

### 2.3 Donations: cart and orders

```mermaid
erDiagram
    cart {
        uuid id PK
        uuid customer_id FK "nullable = guest; CASCADE"
        timestamptz created_at
        timestamptz updated_at
        timestamptz expires_at "guest: +30 days idle"
    }
    cart_line {
        uuid id PK
        uuid cart_id FK "CASCADE"
        text target_kind "project | category (D-01)"
        uuid project_id FK "set iff target_kind=project"
        uuid category_id FK "set iff target_kind=category"
        uuid option_id FK "only with project"
        int quantity "1-999; 1 for amount lines"
        numeric unit_amount "> 0"
        numeric amount "= unit_amount * quantity"
        timestamptz created_at
        timestamptz updated_at
    }
    orders {
        uuid id PK
        text reference UK "T3-YY-NNNNNN (order_ref_seq)"
        uuid customer_id FK
        text status "pending_payment | paid | payment_review | cancelled | expired"
        text donor_name
        citext donor_email
        text donor_mobile "snapshot"
        char locale
        numeric total_amount "= sum(order_line.amount)"
        int line_count
        char status_token_hash "for the guest status page"
        timestamptz payment_due_at "+60 min"
        timestamptz created_at
        timestamptz updated_at
        timestamptz paid_at "R5-Payments"
        timestamptz cancelled_at
        text cancelled_by
        xid xmin
    }
    order_line {
        uuid id PK
        uuid order_id FK "CASCADE"
        smallint line_no
        text target_kind "project | category"
        uuid project_id FK "RESTRICT"
        uuid category_id FK "RESTRICT"
        uuid option_id FK "SET NULL"
        text title_ar "snapshot"
        text title_en
        text option_label_ar "snapshot"
        text option_label_en
        int quantity
        numeric unit_amount
        numeric amount
    }
```

**Constraints:**

- `ck_line_target` (on both `cart_line` and `order_line`):

  ```sql
  (target_kind = 'project'  AND project_id IS NOT NULL AND category_id IS NULL)
  OR
  (target_kind = 'category' AND category_id IS NOT NULL AND project_id IS NULL AND option_id IS NULL)
  ```

- `ck_line_amount`: `amount = unit_amount * quantity AND quantity BETWEEN 1 AND 999 AND unit_amount > 0`.
- `ux_cart_customer (customer_id) WHERE customer_id IS NOT NULL`: one cart per donor.
- `ux_cart_option_line (cart_id, option_id) WHERE option_id IS NOT NULL`: option lines merge (D-04).
- `ix_orders_customer (customer_id, created_at DESC)`, `ix_orders_status (status, payment_due_at)`.
- `ix_order_line_project (project_id)`, `ix_order_line_category (category_id)`.
- **Sequence:** `order_ref_seq`.
- `status` already allows the R5-Payments values, so no enum migration is needed later. R5-Core only sets `pending_payment` and `cancelled`.

### 2.4 Platform

```mermaid
erDiagram
    setting {
        text key PK
        jsonb value
        timestamptz updated_at
        text updated_by
    }
    audit_log {
        bigint id PK
        timestamptz at
        text actor "staff email | system | customer:{id}"
        text action "e.g. project.publish, staff.login_failed"
        text entity
        text entity_id
        jsonb diff
        char ip_hash
    }
    idempotency_key {
        text scope PK "checkout:{customerId}"
        text key PK
        char request_hash
        text status "in_progress | completed"
        int response_status
        jsonb response_body
        timestamptz created_at
        timestamptz expires_at "+24 h"
    }
    outbox_message {
        uuid id PK
        text type "OtpRequested | CatalogChanged | ..."
        jsonb payload
        timestamptz occurred_at
        int attempts
        timestamptz next_attempt_at
        timestamptz claimed_until "lease"
        timestamptz processed_at
        text last_error
        bool dead_lettered
    }
    message_template {
        text key PK
        text channel PK "sms | email"
        char locale PK "ar | en"
        text subject
        text body "Scriban"
        timestamptz updated_at
        text updated_by
    }
    notification {
        uuid id PK
        text channel
        text recipient_hash
        text template_key
        text dedupe_key UK
        text status "sent | failed"
        text provider_ref
        timestamptz created_at
    }
```

**Settings seeded:**

| Key | Default |
|---|---|
| `min_gift_amount` | 10.00 |
| `max_line_amount` | 100000.00 |
| `cart_max_lines` | 50 |
| `cart_max_total` | 100000.00 |
| `payment_window_minutes` | 60 |
| `amount_presets` | 50, 100, 500 |
| `site_name_ar` / `site_name_en`, `support_mobile` | — |

**View `v_catalog_stats`** (staff only): for each project and category, the number of `pending_payment` orders. Paid totals arrive in R5-Payments.

---

## 3. R5-Payments (next session): shown for context, not built now

```mermaid
erDiagram
    orders ||--o{ payment : "attempts"
    payment ||--o{ payment_event : "webhooks (unique provider_event_id)"
    orders ||--o{ refund : ""
    refund ||--o{ refund_line : ""
    order_line ||--o{ refund_line : ""
    orders ||--o{ donation_receipt : "DR / DRC"
    project ||--o| giving_total : "paid totals"
    category ||--o| giving_total : "paid totals"
    document_counter ||--o{ donation_receipt : "gapless numbers"
```

| Table | Purpose |
|---|---|
| `payment`, `payment_event` | Hosted-page payment attempts. The webhook is the truth. The unique `(provider, provider_event_id)` makes events idempotent |
| `refund`, `refund_line` | Finance-only refunds |
| `donation_receipt` | DR (receipt) and DRC (cancellation) PDFs |
| `document_counter` | Gapless numbering per series and year |
| `giving_total` | `target_kind` + `target_id`: `paid_amount`, `donor_count`, `last_gift_at`. Updated inside the settlement transaction and reconciled nightly |

---

## 4. Removed against the current repo (49 tables)

| Table | Status |
|---|---|
| `course_detail`, `course_module`, `book_detail`, `book_format`, `stock_movement`, `customer_address`, `invoice`, `enrolment`, `download_grant`, `download_log`, `shipment` | Removed (commerce) |
| `sponsorship_offer`, `sponsored_unit`, `sponsored_unit_event`, `sponsorship_pool`, `pool_contribution`, `unit_funding`, `distribution`, `beneficiary`, `allocation_batch` | Removed (sponsorship) |
| `cause_detail`, `goal_progress`, `goal_adjustment`, `cause_update`, `dedication`, `zakat_ledger` | Removed. **They're not in the MVP** and can be re-added later as new migrations |
| `product`, `cart_line`, `order_line` | Replaced by `project`, plus the redesigned `cart_line` and `order_line` |
| `payment`, `payment_event`, `refund`, `refund_line`, `donation_receipt`, `document_counter` | Moved to R5-Payments |
| `v_goal`, `v_sales_daily`, `v_allocation_backlog`, `v_zakat_balance` | Removed |

The old migrations (`InitialSchema`, `GrantPaymentEventColumnUpdate`) are deleted and **replaced by one new `InitialSchema`**. This is allowed because nothing has been deployed: no staging or production database exists.
