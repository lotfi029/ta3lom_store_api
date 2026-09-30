# Store Data Dictionary

**Generated from** the EF Core migrations (identical to [`db/reference/001_store_schema.sql`](../../db/reference/001_store_schema.sql), checked by `SchemaParityTests`) on PostgreSQL · 2026-09-30

Every column is listed. **Key**: PK primary key, FK→ foreign key target, UQ unique. Types are PostgreSQL types. The EF Core CLR type mapping is in [01-database-design.md §6](01-database-design.md#6-ef-core-mapping).

Regenerate after each migration with `tools/scripts/regenerate-data-dictionary.sh` so that this file never drifts from the schema.

## Contents

- **1. Settings, audit, numbering** — [`setting`](#setting), [`audit_log`](#audit_log), [`document_counter`](#document_counter)
- **2. Media** — [`media_asset`](#media_asset)
- **3. Catalogue** — [`category`](#category), [`product`](#product), [`course_detail`](#course_detail), [`course_module`](#course_module), [`book_detail`](#book_detail), [`book_format`](#book_format), [`stock_movement`](#stock_movement), [`sponsorship_offer`](#sponsorship_offer), [`cause_detail`](#cause_detail), [`goal_progress`](#goal_progress), [`cause_update`](#cause_update), [`goal_adjustment`](#goal_adjustment)
- **4. Identity** — [`customer`](#customer), [`customer_address`](#customer_address), [`otp_challenge`](#otp_challenge), [`staff_user`](#staff_user), [`refresh_token`](#refresh_token)
- **5. Cart** — [`cart`](#cart), [`cart_line`](#cart_line)
- **6. Ordering** — [`orders`](#orders), [`order_line`](#order_line), [`dedication`](#dedication)
- **7. Payments** — [`payment`](#payment), [`payment_event`](#payment_event), [`refund`](#refund), [`refund_line`](#refund_line), [`idempotency_key`](#idempotency_key)
- **8. Documents** — [`invoice`](#invoice), [`donation_receipt`](#donation_receipt)
- **9. Fulfilment** — [`enrolment`](#enrolment), [`download_grant`](#download_grant), [`download_log`](#download_log), [`shipment`](#shipment)
- **10. Giving** — [`beneficiary`](#beneficiary), [`allocation_batch`](#allocation_batch), [`sponsored_unit`](#sponsored_unit), [`sponsored_unit_event`](#sponsored_unit_event), [`sponsorship_pool`](#sponsorship_pool), [`pool_contribution`](#pool_contribution), [`unit_funding`](#unit_funding), [`distribution`](#distribution), [`zakat_ledger`](#zakat_ledger)
- **11. Notifications** — [`outbox_message`](#outbox_message), [`message_template`](#message_template), [`notification`](#notification)

---

## 1. Settings, audit, numbering

### setting

Admin-editable configuration (Docs/store/03-business-rules.md §M). One JSON value per key.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `key` | text |  |  | PK |  |
| `value` | jsonb |  |  |  |  |
| `updated_at` | timestamp with time zone |  | `now()` |  |  |
| `updated_by` | text | ✓ |  |  |  |

### audit_log

Append-only record of every staff mutation and sensitive system action (BR-S11). Never updated or deleted.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | bigint |  |  | PK |  |
| `at` | timestamp with time zone |  | `now()` |  |  |
| `actor` | text |  |  |  | staff email / 'system' / 'customer:{id}' |
| `action` | text |  |  |  | e.g. product.publish, unit.allocate |
| `entity` | text |  |  |  |  |
| `entity_id` | text | ✓ |  |  |  |
| `diff` | jsonb | ✓ |  |  |  |
| `ip_hash` | character(64) | ✓ |  |  |  |

### document_counter

Gapless counters per document series and year (RUL-59). Row is locked `FOR UPDATE` inside the payment transaction.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `series` | text |  |  | PK |  |
| `year` | integer |  |  | PK |  |
| `last_value` | integer |  | `0` |  |  |

---

## 2. Media

### media_asset

Every stored file: product images, PDFs, private book files. Alt text is mandatory for images (main-site FR-35).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `kind` | text |  |  |  |  |
| `storage_key` | text |  |  | UQ | path under the private/public volume |
| `is_public` | boolean |  | `true` |  | book_file is always private |
| `mime` | text |  |  |  |  |
| `size_bytes` | bigint |  |  |  |  |
| `width` | integer | ✓ |  |  |  |
| `height` | integer | ✓ |  |  |  |
| `alt_ar` | text | ✓ |  |  |  |
| `alt_en` | text | ✓ |  |  |  |
| `created_at` | timestamp with time zone |  | `now()` |  |  |

---

## 3. Catalogue

### category

Course and book categories (filter chips).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `slug` | text |  |  | UQ |  |
| `name_ar` | text |  |  |  |  |
| `name_en` | text | ✓ |  |  |  |
| `sort` | integer |  | `0` |  |  |
| `is_active` | boolean |  | `true` |  |  |

### product

Supertype for every sellable or givable item (RUL-01). Type-specific data lives in `course_detail`, `book_detail`/`book_format`, `cause_detail`.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `type` | text |  |  |  |  |
| `slug` | text |  |  | UQ |  |
| `status` | text |  | `'draft'::text` |  |  |
| `category_id` | uuid | ✓ |  | FK→category |  |
| `cover_asset_id` | uuid | ✓ |  | FK→media_asset |  |
| `title_ar` | text |  |  |  |  |
| `title_en` | text | ✓ |  |  |  |
| `summary_ar` | text |  |  |  |  |
| `summary_en` | text | ✓ |  |  |  |
| `body_ar` | text | ✓ |  |  | Markdown (ADR-008 allow-list) |
| `body_en` | text | ✓ |  |  |  |
| `badge` | text | ✓ |  |  |  |
| `sort` | integer |  | `0` |  |  |
| `seo_title_ar` | text | ✓ |  |  |  |
| `seo_title_en` | text | ✓ |  |  |  |
| `seo_desc_ar` | text | ✓ |  |  |  |
| `seo_desc_en` | text | ✓ |  |  |  |
| `published_at` | timestamp with time zone | ✓ |  |  |  |
| `created_at` | timestamp with time zone |  | `now()` |  |  |
| `updated_at` | timestamp with time zone |  | `now()` |  |  |
| `search_vector` | tsvector | ✓ |  |  |  |

### course_detail

Course price and metadata (1:1 with product).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `product_id` | uuid |  |  | PK FK→product |  |
| `price` | numeric(12,2) |  |  |  |  |
| `compare_at_price` | numeric(12,2) | ✓ |  |  |  |
| `lessons` | integer |  | `0` |  |  |
| `duration_minutes` | integer |  | `0` |  |  |
| `level_ar` | text | ✓ |  |  |  |
| `level_en` | text | ✓ |  |  |  |
| `instructor_ar` | text |  |  |  |  |
| `instructor_en` | text | ✓ |  |  |  |
| `access_days` | integer | ✓ |  |  | NULL = lifetime (RUL-42) |
| `lms_course_ref` | text | ✓ |  |  | LMS adapter key (Q-05) |
| `learner_count` | integer |  | `0` |  | display counter |

### course_module

Curriculum rows shown on the product page.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `product_id` | uuid |  |  | FK→product UQ |  |
| `sort` | integer |  |  | UQ |  |
| `title_ar` | text |  |  |  |  |
| `title_en` | text | ✓ |  |  |  |
| `duration_seconds` | integer |  | `0` |  |  |

### book_detail

Book metadata (1:1 with product).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `product_id` | uuid |  |  | PK FK→product |  |
| `author_ar` | text |  |  |  |  |
| `author_en` | text | ✓ |  |  |  |
| `pages` | integer | ✓ |  |  |  |
| `isbn` | text | ✓ |  |  |  |
| `published_year` | integer | ✓ |  |  |  |

### book_format

Print or digital edition with its own price; print carries stock (RUL-05, RUL-30).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `product_id` | uuid |  |  | FK→product UQ |  |
| `kind` | text |  |  | UQ |  |
| `sku` | text |  |  | UQ |  |
| `price` | numeric(12,2) |  |  |  |  |
| `compare_at_price` | numeric(12,2) | ✓ |  |  |  |
| `is_active` | boolean |  | `true` |  |  |
| `stock_on_hand` | integer | ✓ |  |  |  |
| `stock_reserved` | integer |  | `0` |  |  |
| `weight_grams` | integer | ✓ |  |  |  |
| `low_stock_threshold` | integer | ✓ |  |  | NULL = setting low_stock_default_threshold |
| `file_asset_id` | uuid | ✓ |  | FK→media_asset |  |

### stock_movement

Ledger of every stock change. `book_format.stock_on_hand` must equal the sum of movements (nightly check).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | bigint |  |  | PK |  |
| `book_format_id` | uuid |  |  | FK→book_format |  |
| `delta` | integer |  |  |  |  |
| `reason` | text |  |  |  |  |
| `ref_type` | text | ✓ |  |  | order / allocation / manual |
| `ref_id` | text | ✓ |  |  |  |
| `note` | text | ✓ |  |  |  |
| `at` | timestamp with time zone |  | `now()` |  |  |
| `actor` | text |  |  |  |  |

### sponsorship_offer

Makes a course or book sponsorable (RUL-06). At most one active offer per product.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `product_id` | uuid |  |  | FK→product |  |
| `seat_price` | numeric(12,2) |  |  |  |  |
| `unit_label_ar` | text |  | `'مقعد'::text` |  |  |
| `unit_label_en` | text |  | `'seat'::text` |  |  |
| `target_units` | integer |  |  |  |  |
| `beneficiary_group` | text |  |  |  | machine key used by allocation matching (RUL-47b) |
| `group_label_ar` | text |  |  |  |  |
| `group_label_en` | text | ✓ |  |  |  |
| `allow_partial` | boolean |  | `true` |  |  |
| `zakat_eligible` | boolean |  | `false` |  | S2 |
| `close_on_target` | boolean |  | `false` |  |  |
| `starts_at` | timestamp with time zone |  | `now()` |  |  |
| `ends_at` | timestamp with time zone |  |  |  |  |
| `is_active` | boolean |  | `true` |  |  |
| `created_at` | timestamp with time zone |  | `now()` |  |  |

### cause_detail

Donation product settings (RUL-07). Exactly one row may be the general fund.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `product_id` | uuid |  |  | PK FK→product |  |
| `unit_amount` | numeric(12,2) | ✓ |  |  |  |
| `unit_label_ar` | text | ✓ |  |  |  |
| `unit_label_en` | text | ✓ |  |  |  |
| `target_amount` | numeric(14,2) |  |  |  |  |
| `allow_custom_amount` | boolean |  | `true` |  |  |
| `min_amount` | numeric(12,2) |  | `10` |  |  |
| `zakat_eligible` | boolean |  | `false` |  |  |
| `beneficiaries_ar` | text | ✓ |  |  |  |
| `beneficiaries_en` | text | ✓ |  |  |  |
| `close_on_target` | boolean |  | `false` |  |  |
| `is_general_fund` | boolean |  | `false` |  | receives pool remainders (RUL-46) |
| `starts_at` | timestamp with time zone |  | `now()` |  |  |
| `ends_at` | timestamp with time zone | ✓ |  |  | NULL = open-ended (general fund) |

### goal_progress

Denormalised counters for progress bars (RUL-08). Updated in the payment transaction; `seed_*` holds pre-launch offline figures.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `product_id` | uuid |  |  | PK FK→product |  |
| `raised_amount` | numeric(14,2) |  | `0` |  | causes, and money for sponsorships |
| `funded_units` | integer |  | `0` |  | sponsorship units created |
| `donor_count` | integer |  | `0` |  | distinct paid orders |
| `zakat_amount` | numeric(14,2) |  | `0` |  |  |
| `seed_raised` | numeric(14,2) |  | `0` |  | opening balance migrated from offline giving |
| `seed_units` | integer |  | `0` |  |  |
| `seed_donors` | integer |  | `0` |  |  |
| `last_gift_at` | timestamp with time zone | ✓ |  |  |  |
| `updated_at` | timestamp with time zone |  | `now()` |  |  |

### cause_update

Delivery updates posted by staff (RUL-54).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `product_id` | uuid |  |  | FK→product |  |
| `title_ar` | text |  |  |  |  |
| `title_en` | text | ✓ |  |  |  |
| `body_ar` | text | ✓ |  |  |  |
| `body_en` | text | ✓ |  |  |  |
| `asset_id` | uuid | ✓ |  | FK→media_asset |  |
| `units_delivered` | integer | ✓ |  |  |  |
| `occurred_on` | date |  |  |  |  |
| `is_public` | boolean |  | `true` |  |  |
| `notify_donors` | boolean |  | `false` |  |  |
| `notified_at` | timestamp with time zone | ✓ |  |  |  |
| `created_at` | timestamp with time zone |  | `now()` |  |  |
| `created_by` | text |  |  |  |  |

### goal_adjustment

Goal movements that are not order lines: pool sweep out/in pairs (RUL-46) and audited finance corrections. Included in integrity check 6.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | bigint |  |  | PK |  |
| `product_id` | uuid |  |  | FK→product |  |
| `kind` | text |  |  |  |  |
| `amount` | numeric(12,2) |  |  |  | signed: negative for sweep_out |
| `units` | integer |  | `0` |  |  |
| `sponsorship_offer_id` | uuid | ✓ |  | FK→sponsorship_offer |  |
| `pair_id` | uuid | ✓ |  |  | links the out/in rows of one sweep |
| `note` | text | ✓ |  |  |  |
| `at` | timestamp with time zone |  | `now()` |  |  |
| `actor` | text |  |  |  |  |

---

## 4. Identity

### customer

Store customer, keyed by mobile (RUL-60/61). Separate from main-site editors.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `mobile` | text |  |  | UQ |  |
| `full_name` | text | ✓ |  |  |  |
| `email` | citext | ✓ |  |  |  |
| `locale` | character(2) |  | `'ar'::bpchar` |  |  |
| `anonymous_default` | boolean |  | `false` |  |  |
| `marketing_opt_in` | boolean |  | `false` |  |  |
| `status` | text |  | `'active'::text` |  |  |
| `pdpl_consent_at` | timestamp with time zone | ✓ |  |  |  |
| `last_login_at` | timestamp with time zone | ✓ |  |  |  |
| `created_at` | timestamp with time zone |  | `now()` |  |  |
| `updated_at` | timestamp with time zone |  | `now()` |  |  |

### customer_address

Saved shipping addresses (Saudi National Address format).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `customer_id` | uuid |  |  | FK→customer |  |
| `recipient_name` | text |  |  |  |  |
| `recipient_mobile` | text |  |  |  |  |
| `city` | text |  |  |  |  |
| `district` | text | ✓ |  |  |  |
| `short_address` | text | ✓ |  |  |  |
| `street` | text | ✓ |  |  |  |
| `building_no` | text | ✓ |  |  |  |
| `postal_code` | text | ✓ |  |  |  |
| `is_default` | boolean |  | `false` |  |  |
| `created_at` | timestamp with time zone |  | `now()` |  |  |

### otp_challenge

One row per OTP sent. Code stored as HMAC; rate limits are computed from this table (RUL-60).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `mobile` | text |  |  |  |  |
| `purpose` | text |  |  |  |  |
| `code_hash` | character(64) |  |  |  | HMAC-SHA256(code, server pepper) |
| `attempts` | smallint |  | `0` |  |  |
| `expires_at` | timestamp with time zone |  |  |  |  |
| `consumed_at` | timestamp with time zone | ✓ |  |  |  |
| `ip_hash` | character(64) | ✓ |  |  |  |
| `created_at` | timestamp with time zone |  | `now()` |  |  |

### staff_user

Admin users with roles and mandatory TOTP (RUL-62).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `email` | citext |  |  | UQ |  |
| `name` | text |  |  |  |  |
| `pass_hash` | text |  |  |  | ASP.NET Core PasswordHasher v3 (PBKDF2) or Argon2id |
| `totp_secret_enc` | bytea | ✓ |  |  | Data Protection-encrypted; NULL until enrolled |
| `roles` | _text |  | `'{viewer}'::text[]` |  |  |
| `failed_count` | integer |  | `0` |  |  |
| `locked_until` | timestamp with time zone | ✓ |  |  |  |
| `is_active` | boolean |  | `true` |  |  |
| `last_login_at` | timestamp with time zone | ✓ |  |  |  |
| `created_at` | timestamp with time zone |  | `now()` |  |  |

### refresh_token

Rotating refresh tokens for customers and staff, with family-based reuse detection (ADR-006 pattern).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `subject_type` | text |  |  |  |  |
| `subject_id` | uuid |  |  |  |  |
| `token_hash` | character(64) |  |  | UQ | SHA-256; raw token only in the cookie |
| `family_id` | uuid |  |  |  | reuse detection revokes the family |
| `expires_at` | timestamp with time zone |  |  |  |  |
| `revoked_at` | timestamp with time zone | ✓ |  |  |  |
| `replaced_by` | uuid | ✓ |  |  |  |
| `created_at` | timestamp with time zone |  | `now()` |  |  |
| `user_agent` | text | ✓ |  |  |  |

---

## 5. Cart

### cart

Server-side cart; guests via HttpOnly cookie id (RUL-11).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK | opaque id in the HttpOnly cookie |
| `customer_id` | uuid | ✓ |  | FK→customer |  |
| `status` | text |  | `'active'::text` |  |  |
| `expires_at` | timestamp with time zone | ✓ |  |  | guests only (RUL-16) |
| `created_at` | timestamp with time zone |  | `now()` |  |  |
| `updated_at` | timestamp with time zone |  | `now()` |  |  |

### cart_line

Indicative lines — repriced at checkout (RUL-15). Dedication kept as JSON until the order is created.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `cart_id` | uuid |  |  | FK→cart |  |
| `product_id` | uuid |  |  | FK→product |  |
| `book_format_id` | uuid | ✓ |  | FK→book_format |  |
| `kind` | text |  |  |  |  |
| `mode` | text |  |  |  |  |
| `quantity` | integer |  | `1` |  |  |
| `unit_price` | numeric(12,2) |  |  |  | indicative (RUL-15) |
| `amount` | numeric(12,2) |  |  |  |  |
| `intent` | text | ✓ |  |  |  |
| `dedication` | jsonb | ✓ |  |  | {toName,toMobile,fromName,templateKey,text}; validated in app |
| `added_at` | timestamp with time zone |  | `now()` |  |  |

---

## 6. Ordering

### orders

One checkout. Named in the plural because ORDER is a reserved word. Totals are snapshots (RUL-31). The state machine is in RUL-29.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `reference` | text |  |  | UQ | T3-26-001234 (RUL-28) |
| `customer_id` | uuid |  |  | FK→customer |  |
| `status` | text |  | `'pending_payment'::text` |  |  |
| `currency` | character(3) |  | `'SAR'::bpchar` |  |  |
| `purchases_total` | numeric(12,2) |  | `0` |  |  |
| `shipping_total` | numeric(12,2) |  | `0` |  |  |
| `gifts_total` | numeric(12,2) |  | `0` |  |  |
| `vat_total` | numeric(12,2) |  | `0` |  | informational: included in purchases/shipping |
| `grand_total` | numeric(12,2) |  |  |  |  |
| `refunded_total` | numeric(12,2) |  | `0` |  |  |
| `is_anonymous` | boolean |  | `false` |  |  |
| `locale` | character(2) |  | `'ar'::bpchar` |  |  |
| `contact_name` | text |  |  |  |  |
| `contact_mobile` | text |  |  |  |  |
| `contact_email` | citext |  |  |  |  |
| `shipping_address` | jsonb | ✓ |  |  | snapshot; required if a print line exists |
| `source_cart_id` | uuid | ✓ |  |  |  |
| `terms_accepted_at` | timestamp with time zone |  |  |  | FR-S30, E-Commerce Law evidence |
| `placed_at` | timestamp with time zone |  | `now()` |  |  |
| `payment_due_at` | timestamp with time zone |  |  |  | placed_at + timeout (RUL-29) |
| `paid_at` | timestamp with time zone | ✓ |  |  |  |
| `fulfilled_at` | timestamp with time zone | ✓ |  |  |  |
| `cancelled_at` | timestamp with time zone | ✓ |  |  |  |

### order_line

Immutable snapshot of what was bought or given, with VAT split and fulfilment status (RUL-40).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `order_id` | uuid |  |  | FK→orders UQ |  |
| `line_no` | smallint |  |  | UQ |  |
| `product_id` | uuid |  |  | FK→product |  |
| `product_type` | text |  |  |  |  |
| `book_format_id` | uuid | ✓ |  | FK→book_format |  |
| `sponsorship_offer_id` | uuid | ✓ |  | FK→sponsorship_offer |  |
| `kind` | text |  |  |  |  |
| `mode` | text |  |  |  |  |
| `quantity` | integer |  |  |  |  |
| `unit_price` | numeric(12,2) |  |  |  |  |
| `amount` | numeric(12,2) |  |  |  |  |
| `vat_rate` | numeric(5,2) |  | `0` |  |  |
| `vat_amount` | numeric(12,2) |  | `0` |  |  |
| `intent` | text | ✓ |  |  |  |
| `title_ar` | text |  |  |  | snapshot at order time |
| `title_en` | text | ✓ |  |  |  |
| `fulfilment_status` | text |  | `'pending'::text` |  |  |
| `refunded_amount` | numeric(12,2) |  | `0` |  |  |

### dedication

إهداء attached to a gift line; `card_token` drives the public card page (RUL-21/22).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `order_line_id` | uuid |  |  | FK→order_line UQ |  |
| `to_name` | text |  |  |  |  |
| `to_mobile` | text |  |  |  |  |
| `from_name` | text | ✓ |  |  |  |
| `template_key` | text |  |  |  |  |
| `custom_text` | text | ✓ |  |  |  |
| `card_token` | text |  |  | UQ | 22-char url-safe random, public card URL |
| `sms_status` | text |  | `'pending'::text` |  |  |
| `sent_at` | timestamp with time zone | ✓ |  |  |  |

---

## 7. Payments

### payment

One attempt at the gateway. Several per order are possible (retries, RUL-37).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `order_id` | uuid |  |  | FK→orders |  |
| `provider` | text |  |  | UQ | moyasar / hyperpay / tap … (Q-01) |
| `provider_payment_id` | text | ✓ |  | UQ |  |
| `method` | text | ✓ |  |  |  |
| `amount` | numeric(12,2) |  |  |  |  |
| `status` | text |  | `'initiated'::text` |  |  |
| `failure_code` | text | ✓ |  |  |  |
| `failure_message` | text | ✓ |  |  |  |
| `created_at` | timestamp with time zone |  | `now()` |  |  |
| `updated_at` | timestamp with time zone |  | `now()` |  |  |

### payment_event

Webhook inbox: every callback stored before processing; unique per provider event (RUL-34).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | bigint |  |  | PK |  |
| `provider` | text |  |  | UQ |  |
| `provider_event_id` | text |  |  | UQ |  |
| `event_type` | text |  |  |  |  |
| `signature_valid` | boolean |  |  |  |  |
| `payload` | jsonb |  |  |  |  |
| `received_at` | timestamp with time zone |  | `now()` |  |  |
| `processed_at` | timestamp with time zone | ✓ |  |  |  |
| `error` | text | ✓ |  |  |  |

### refund

Refund issued through the gateway by finance (RUL-56).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `order_id` | uuid |  |  | FK→orders |  |
| `payment_id` | uuid |  |  | FK→payment |  |
| `amount` | numeric(12,2) |  |  |  |  |
| `reason` | text |  |  |  |  |
| `status` | text |  | `'requested'::text` |  |  |
| `provider_refund_id` | text | ✓ |  |  |  |
| `requested_by` | text |  |  |  | staff email (finance role) |
| `created_at` | timestamp with time zone |  | `now()` |  |  |
| `completed_at` | timestamp with time zone | ✓ |  |  |  |

### refund_line

Which lines, amounts and units a refund covers.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `refund_id` | uuid |  |  | PK FK→refund |  |
| `order_line_id` | uuid |  |  | PK FK→order_line |  |
| `amount` | numeric(12,2) |  |  |  |  |
| `units` | integer | ✓ |  |  | sponsored units cancelled, if any |

### idempotency_key

Stored responses for `Idempotency-Key` requests (checkout, refunds); pruned after 24 h.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `key` | text |  |  | PK |  |
| `scope` | text |  |  | PK | e.g. checkout:{customerId}, refund:{staffId} |
| `request_hash` | character(64) |  |  |  | SHA-256 of the canonical request body |
| `status_code` | integer | ✓ |  |  |  |
| `response` | jsonb | ✓ |  |  |  |
| `created_at` | timestamp with time zone |  | `now()` |  |  |

---

## 8. Documents

### invoice

Sales invoice or credit note for purchases. ZATCA columns are filled in S2 (BR-S13).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `order_id` | uuid |  |  | FK→orders |  |
| `kind` | text |  |  |  |  |
| `number` | text |  |  | UQ | INV-2026-000001 / CN-2026-000001 |
| `issued_at` | timestamp with time zone |  | `now()` |  |  |
| `is_tax_invoice` | boolean |  |  |  | vat_enabled at issue time |
| `seller_vat_no` | text | ✓ |  |  |  |
| `net_total` | numeric(12,2) |  |  |  |  |
| `vat_total` | numeric(12,2) |  |  |  |  |
| `gross_total` | numeric(12,2) |  |  |  |  |
| `references_invoice_id` | uuid | ✓ |  | FK→invoice | credit note → original |
| `pdf_asset_id` | uuid | ✓ |  | FK→media_asset |  |
| `zatca_uuid` | uuid | ✓ |  |  |  |
| `zatca_icv` | bigint | ✓ |  |  | invoice counter value |
| `zatca_pih` | text | ✓ |  |  | previous invoice hash |
| `zatca_hash` | text | ✓ |  |  |  |
| `zatca_qr` | text | ✓ |  |  | TLV base64 |
| `zatca_status` | text | ✓ |  |  |  |

### donation_receipt

Donation receipt or cancellation for gift lines (RUL-59). Zakat is shown separately.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `order_id` | uuid |  |  | FK→orders |  |
| `kind` | text |  | `'receipt'::text` |  |  |
| `number` | text |  |  | UQ | DR-2026-000001 / DRC-… |
| `issued_at` | timestamp with time zone |  | `now()` |  |  |
| `donor_name` | text |  |  |  | real name even when anonymous (RUL-23) |
| `total_amount` | numeric(12,2) |  |  |  |  |
| `zakat_amount` | numeric(12,2) |  | `0` |  |  |
| `references_receipt_id` | uuid | ✓ |  | FK→donation_receipt |  |
| `pdf_asset_id` | uuid | ✓ |  | FK→media_asset |  |

---

## 9. Fulfilment

### enrolment

Access to a course, from a purchase or a sponsored allocation. One live enrolment per customer per course (RUL-13, RUL-47c).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `customer_id` | uuid |  |  | FK→customer |  |
| `product_id` | uuid |  |  | FK→product |  |
| `source` | text |  |  |  |  |
| `order_line_id` | uuid | ✓ |  | FK→order_line | purchase |
| `sponsored_unit_id` | uuid | ✓ |  | FK→sponsored_unit | sponsored (FK added below) |
| `status` | text |  | `'active'::text` |  |  |
| `progress_pct` | smallint |  | `0` |  |  |
| `lms_enrolment_ref` | text | ✓ |  |  |  |
| `granted_at` | timestamp with time zone |  | `now()` |  |  |
| `expires_at` | timestamp with time zone | ✓ |  |  |  |
| `started_at` | timestamp with time zone | ✓ |  |  |  |
| `completed_at` | timestamp with time zone | ✓ |  |  |  |
| `certificate_no` | text | ✓ |  | UQ |  |

### download_grant

Right to download a digital book (RUL-41).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `customer_id` | uuid |  |  | FK→customer |  |
| `order_line_id` | uuid |  |  | FK→order_line UQ |  |
| `book_format_id` | uuid |  |  | FK→book_format |  |
| `max_downloads` | integer |  | `5` |  |  |
| `download_count` | integer |  | `0` |  |  |
| `revoked_at` | timestamp with time zone | ✓ |  |  |  |
| `created_at` | timestamp with time zone |  | `now()` |  |  |

### download_log

Each download, for limits and abuse review.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | bigint |  |  | PK |  |
| `grant_id` | uuid |  |  | FK→download_grant |  |
| `at` | timestamp with time zone |  | `now()` |  |  |
| `ip_hash` | character(64) | ✓ |  |  |  |

### shipment

One shipment per order containing print lines.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `order_id` | uuid |  |  | FK→orders UQ |  |
| `status` | text |  | `'preparing'::text` |  |  |
| `carrier` | text | ✓ |  |  |  |
| `tracking_no` | text | ✓ |  |  |  |
| `address` | jsonb |  |  |  |  |
| `shipped_at` | timestamp with time zone | ✓ |  |  |  |
| `delivered_at` | timestamp with time zone | ✓ |  |  |  |
| `updated_by` | text | ✓ |  |  |  |

---

## 10. Giving

### beneficiary

Register of people and organisations who receive sponsored units. Personal data encrypted at the application layer (PDPL).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `code` | text |  |  | UQ | B-000123 |
| `kind` | text |  |  |  |  |
| `beneficiary_group` | text |  |  |  | matches sponsorship_offer.beneficiary_group |
| `region` | text |  |  |  | e.g. القصيم (shown to donors) |
| `full_name_enc` | bytea |  |  |  | Data Protection-encrypted PII (PDPL) |
| `mobile_enc` | bytea | ✓ |  |  |  |
| `mobile_hash` | character(64) | ✓ |  |  | lookup without decrypting |
| `guardian_name_enc` | bytea | ✓ |  |  |  |
| `organisation_name` | text | ✓ |  |  | school / library / home (not personal) |
| `eligibility_status` | text |  | `'pending'::text` |  |  |
| `zakat_eligible` | boolean |  | `false` |  |  |
| `eligibility_notes` | text | ✓ |  |  |  |
| `verified_by` | text | ✓ |  |  |  |
| `verified_at` | timestamp with time zone | ✓ |  |  |  |
| `customer_id` | uuid | ✓ |  | FK→customer | linked on first OTP login (RUL-52) |
| `created_at` | timestamp with time zone |  | `now()` |  |  |
| `created_by` | text |  |  |  |  |

### allocation_batch

Groups allocations made together, so they can be notified and audited as one action.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `product_id` | uuid |  |  | FK→product |  |
| `note` | text | ✓ |  |  |  |
| `created_by` | text |  |  |  |  |
| `created_at` | timestamp with time zone |  | `now()` |  |  |

### sponsored_unit

One seat or copy funded by donors (RUL-43/45). Carries the allocation lifecycle (RUL-50).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `product_id` | uuid |  |  | FK→product |  |
| `sponsorship_offer_id` | uuid |  |  | FK→sponsorship_offer |  |
| `source` | text |  |  |  |  |
| `order_line_id` | uuid | ✓ |  | FK→order_line | full units: the funding line |
| `unit_price` | numeric(12,2) |  |  |  |  |
| `has_zakat` | boolean |  | `false` |  | RUL-47d |
| `status` | text |  | `'awaiting_allocation'::text` |  |  |
| `beneficiary_id` | uuid | ✓ |  | FK→beneficiary |  |
| `allocation_batch_id` | uuid | ✓ |  | FK→allocation_batch |  |
| `funded_at` | timestamp with time zone |  | `now()` |  |  |
| `allocated_at` | timestamp with time zone | ✓ |  |  |  |
| `allocated_by` | text | ✓ |  |  |  |
| `completed_at` | timestamp with time zone | ✓ |  |  |  |
| `lapsed_at` | timestamp with time zone | ✓ |  |  |  |

### sponsored_unit_event

Status history shown to sponsors as a timeline.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | bigint |  |  | PK |  |
| `unit_id` | uuid |  |  | FK→sponsored_unit |  |
| `from_status` | text | ✓ |  |  |  |
| `to_status` | text |  |  |  |  |
| `at` | timestamp with time zone |  | `now()` |  |  |
| `actor` | text |  |  |  |  |

### sponsorship_pool

Running balance of partial contributions per offer (RUL-45).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `sponsorship_offer_id` | uuid |  |  | PK FK→sponsorship_offer |  |
| `balance` | numeric(12,2) |  | `0` |  |  |
| `updated_at` | timestamp with time zone |  | `now()` |  |  |

### pool_contribution

A partial contribution; `remaining` shrinks as units are funded, FIFO.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `sponsorship_offer_id` | uuid |  |  | FK→sponsorship_offer |  |
| `order_line_id` | uuid |  |  | FK→order_line UQ |  |
| `amount` | numeric(12,2) |  |  |  |  |
| `remaining` | numeric(12,2) |  |  |  | not yet converted into units |
| `is_zakat` | boolean |  | `false` |  |  |
| `swept_to_fund_at` | timestamp with time zone | ✓ |  |  | RUL-46 |
| `created_at` | timestamp with time zone |  | `now()` |  |  |

### unit_funding

Which contributions funded which pooled unit, and how much each gave.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `sponsored_unit_id` | uuid |  |  | PK FK→sponsored_unit |  |
| `pool_contribution_id` | uuid |  |  | PK FK→pool_contribution |  |
| `amount` | numeric(12,2) |  |  |  |  |

### distribution

Hand-over record for sponsored book copies.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `sponsored_unit_id` | uuid |  |  | FK→sponsored_unit UQ |  |
| `book_format_id` | uuid |  |  | FK→book_format |  |
| `delivered_on` | date | ✓ |  |  |  |
| `proof_asset_id` | uuid | ✓ |  | FK→media_asset |  |

### zakat_ledger

Every zakat movement in, out or refunded (RUL-20).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | bigint |  |  | PK |  |
| `product_id` | uuid |  |  | FK→product |  |
| `direction` | text |  |  |  |  |
| `amount` | numeric(12,2) |  |  |  |  |
| `order_line_id` | uuid | ✓ |  | FK→order_line |  |
| `reference` | text | ✓ |  |  | disbursement voucher / unit id |
| `at` | timestamp with time zone |  | `now()` |  |  |
| `actor` | text |  |  |  |  |

---

## 11. Notifications

### outbox_message

Transactional outbox written in the same transaction as the business change. Worker delivers with retry and backoff (RUL-63).

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `type` | text |  |  |  | OrderPaid, DedicationCard, UnitAllocated … |
| `payload` | jsonb |  |  |  |  |
| `occurred_at` | timestamp with time zone |  | `now()` |  |  |
| `processed_at` | timestamp with time zone | ✓ |  |  |  |
| `attempts` | integer |  | `0` |  |  |
| `next_attempt_at` | timestamp with time zone |  | `now()` |  |  |
| `last_error` | text | ✓ |  |  |  |

### message_template

SMS/email templates per key, channel and locale.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `key` | text |  |  | PK |  |
| `channel` | text |  |  | PK |  |
| `locale` | character(2) |  |  | PK |  |
| `subject` | text | ✓ |  |  |  |
| `body` | text |  |  |  | Scriban/Liquid template |
| `updated_at` | timestamp with time zone |  | `now()` |  |  |

### notification

Delivery record of each SMS or email sent.

| Column | Type | Null | Default | Key | Notes |
|---|---|---|---|---|---|
| `id` | uuid |  | `uuidv7()` | PK |  |
| `channel` | text |  |  |  |  |
| `recipient` | text |  |  |  |  |
| `template_key` | text |  |  |  |  |
| `locale` | character(2) |  |  |  |  |
| `status` | text |  | `'queued'::text` |  |  |
| `provider_msg_id` | text | ✓ |  |  |  |
| `error` | text | ✓ |  |  |  |
| `related_order_id` | uuid | ✓ |  | FK→orders |  |
| `outbox_message_id` | uuid | ✓ |  | FK→outbox_message | dedupe: a retried outbox message never re-sends |
| `created_at` | timestamp with time zone |  | `now()` |  |  |
| `sent_at` | timestamp with time zone | ✓ |  |  |  |

