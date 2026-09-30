-- ============================================================================
--  ta3lom STORE — PostgreSQL 18 reference schema (S1)
--
--  Reference DDL for review and for DBA work, kept by hand with its comments.
--  In the application, the schema is owned by EF Core migrations
--  (src/Ta3lom.Store.Infrastructure/Persistence/Migrations). SchemaParityTests
--  builds one database from this file and one from the migrations and fails on
--  any difference, so a model change edits this file in the same pull request.
--  A pg_dump of the migrated schema: tools/scripts/regenerate-schema-sql.sh
--  (db/reference/generated/). On PostgreSQL < 18, create a uuidv7() shim first
--  (see tests/smoke_scenario.sql); migrations never create it.
--
--  Conventions (Docs/store/database/01-database-design.md §2):
--    * one schema: store
--    * snake_case names (EFCore.NamingConventions)
--    * uuid PKs generated with uuidv7() (PG 18, time-ordered)
--    * money = numeric(12,2) SAR, never float
--    * enums = text + CHECK (cheap to evolve in migrations)
--    * timestamptz everywhere; business dates are interpreted in Asia/Riyadh
--    * optimistic concurrency via the xmin system column (Npgsql UseXminAsConcurrencyToken)
--    * Business rule references: RUL-nn → Docs/store/03-business-rules.md
-- ============================================================================

CREATE SCHEMA IF NOT EXISTS store;
SET search_path = store, public;

CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS pg_trgm;   -- catalogue search

-- Arabic normalisation for search (FR-S03): unify alef forms, taa marbuta, alef maqsura,
-- strip tashkeel and tatweel, lower-case Latin. IMMUTABLE so it can feed a generated column.
CREATE OR REPLACE FUNCTION ar_normalize(t text) RETURNS text
LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
  SELECT lower(translate(
           regexp_replace(coalesce(t,''), '[\u064B-\u0652\u0640]', '', 'g'),
           'أإآٱةى', 'اااايهي'))
$$;

-- ----------------------------------------------------------------------------
--  SEQUENCES — human-facing numbers (RUL-28, RUL-59)
-- ----------------------------------------------------------------------------
CREATE SEQUENCE order_ref_seq       START 1000;
CREATE SEQUENCE beneficiary_code_seq START 1;

-- Gapless document numbering per series+year: counter row locked FOR UPDATE
CREATE TABLE document_counter (
    series      text    NOT NULL CHECK (series IN ('INV','CN','DR','DRC')),
    year        int     NOT NULL,
    last_value  int     NOT NULL DEFAULT 0,
    PRIMARY KEY (series, year)
);

-- ============================================================================
--  1. SETTINGS & AUDIT
-- ============================================================================
CREATE TABLE setting (
    key         text PRIMARY KEY,
    value       jsonb NOT NULL,
    updated_at  timestamptz NOT NULL DEFAULT now(),
    updated_by  text
);

CREATE TABLE audit_log (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    at          timestamptz NOT NULL DEFAULT now(),
    actor       text NOT NULL,                      -- staff email / 'system' / 'customer:{id}'
    action      text NOT NULL,                      -- e.g. product.publish, unit.allocate
    entity      text NOT NULL,
    entity_id   text,
    diff        jsonb,
    ip_hash     char(64)
);
CREATE INDEX ix_audit_entity ON audit_log (entity, entity_id);
CREATE INDEX ix_audit_at     ON audit_log (at DESC);

-- ============================================================================
--  2. MEDIA
-- ============================================================================
CREATE TABLE media_asset (
    id           uuid PRIMARY KEY DEFAULT uuidv7(),
    kind         text NOT NULL CHECK (kind IN ('image','pdf','book_file')),
    storage_key  text NOT NULL UNIQUE,              -- path under the private/public volume
    is_public    boolean NOT NULL DEFAULT true,     -- book_file is always private
    mime         text NOT NULL,
    size_bytes   bigint NOT NULL CHECK (size_bytes > 0),
    width        int,
    height       int,
    alt_ar       text,
    alt_en       text,
    created_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_media_alt CHECK (kind <> 'image' OR alt_ar IS NOT NULL),
    CONSTRAINT ck_media_private_book CHECK (kind <> 'book_file' OR is_public = false)
);

-- ============================================================================
--  3. CATALOGUE (RUL-01 … RUL-10)
-- ============================================================================
CREATE TABLE category (
    id          uuid PRIMARY KEY DEFAULT uuidv7(),
    slug        text NOT NULL UNIQUE CHECK (slug ~ '^[a-z0-9-]{2,60}$'),
    name_ar     text NOT NULL,
    name_en     text,
    sort        int  NOT NULL DEFAULT 0,
    is_active   boolean NOT NULL DEFAULT true
);

CREATE TABLE product (
    id               uuid PRIMARY KEY DEFAULT uuidv7(),
    type             text NOT NULL CHECK (type IN ('course','book','cause')),
    slug             text NOT NULL UNIQUE CHECK (slug ~ '^[a-z0-9-]{2,80}$'),
    status           text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft','published','archived')),
    category_id      uuid REFERENCES category(id) ON DELETE SET NULL,
    cover_asset_id   uuid REFERENCES media_asset(id) ON DELETE SET NULL,
    title_ar         text NOT NULL CHECK (length(title_ar) BETWEEN 2 AND 160),
    title_en         text,
    summary_ar       text NOT NULL,
    summary_en       text,
    body_ar          text,                              -- Markdown (ADR-008 allow-list)
    body_en          text,
    badge            text CHECK (badge IN ('best','new','urgent')),
    sort             int  NOT NULL DEFAULT 0,
    seo_title_ar     text, seo_title_en text,
    seo_desc_ar      text, seo_desc_en  text,
    published_at     timestamptz,
    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz NOT NULL DEFAULT now(),
    search_vector    tsvector GENERATED ALWAYS AS (
        to_tsvector('simple', ar_normalize(coalesce(title_ar,'') || ' ' || coalesce(title_en,'') || ' ' ||
                                           coalesce(summary_ar,'') || ' ' || coalesce(summary_en,'')))) STORED
);
CREATE INDEX ix_product_listing ON product (type, status, sort);
CREATE INDEX ix_product_search  ON product USING gin (search_vector);
CREATE INDEX ix_product_trgm    ON product USING gin (ar_normalize(title_ar) gin_trgm_ops);

-- 3.1 Course details (1:1)
CREATE TABLE course_detail (
    product_id        uuid PRIMARY KEY REFERENCES product(id) ON DELETE CASCADE,
    price             numeric(12,2) NOT NULL CHECK (price >= 0),
    compare_at_price  numeric(12,2) CHECK (compare_at_price IS NULL OR compare_at_price > price),
    lessons           int  NOT NULL DEFAULT 0 CHECK (lessons >= 0),
    duration_minutes  int  NOT NULL DEFAULT 0 CHECK (duration_minutes >= 0),
    level_ar          text, level_en text,
    instructor_ar     text NOT NULL, instructor_en text,
    access_days       int  CHECK (access_days IS NULL OR access_days > 0),   -- NULL = lifetime (RUL-42)
    lms_course_ref    text,                                                  -- LMS adapter key (Q-05)
    learner_count     int  NOT NULL DEFAULT 0                                -- display counter
);

CREATE TABLE course_module (
    id               uuid PRIMARY KEY DEFAULT uuidv7(),
    product_id       uuid NOT NULL REFERENCES product(id) ON DELETE CASCADE,
    sort             int  NOT NULL,
    title_ar         text NOT NULL,
    title_en         text,
    duration_seconds int  NOT NULL DEFAULT 0,
    UNIQUE (product_id, sort)
);

-- 3.2 Book details (1:1) and formats (1:n) — RUL-05
CREATE TABLE book_detail (
    product_id     uuid PRIMARY KEY REFERENCES product(id) ON DELETE CASCADE,
    author_ar      text NOT NULL, author_en text,
    pages          int  CHECK (pages > 0),
    isbn           text,
    published_year int
);

CREATE TABLE book_format (
    id                uuid PRIMARY KEY DEFAULT uuidv7(),
    product_id        uuid NOT NULL REFERENCES product(id) ON DELETE CASCADE,
    kind              text NOT NULL CHECK (kind IN ('print','digital')),
    sku               text NOT NULL UNIQUE,
    price             numeric(12,2) NOT NULL CHECK (price >= 0),
    compare_at_price  numeric(12,2) CHECK (compare_at_price IS NULL OR compare_at_price > price),
    is_active         boolean NOT NULL DEFAULT true,
    -- print only
    stock_on_hand     int CHECK (stock_on_hand >= 0),
    stock_reserved    int NOT NULL DEFAULT 0 CHECK (stock_reserved >= 0),
    weight_grams      int,
    low_stock_threshold int CHECK (low_stock_threshold >= 0),   -- NULL = setting low_stock_default_threshold
    -- digital only
    file_asset_id     uuid REFERENCES media_asset(id),
    UNIQUE (product_id, kind),
    CONSTRAINT ck_format_print   CHECK (kind <> 'print'   OR stock_on_hand IS NOT NULL),
    CONSTRAINT ck_format_digital CHECK (kind <> 'digital' OR file_asset_id IS NOT NULL),
    CONSTRAINT ck_format_reserve CHECK (stock_reserved <= coalesce(stock_on_hand, 0))
);

CREATE TABLE stock_movement (
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    book_format_id  uuid NOT NULL REFERENCES book_format(id),
    delta           int  NOT NULL CHECK (delta <> 0),
    reason          text NOT NULL CHECK (reason IN ('restock','sale','return','sponsor_distribution','adjustment')),
    ref_type        text,                 -- order / allocation / manual
    ref_id          text,
    note            text,
    at              timestamptz NOT NULL DEFAULT now(),
    actor           text NOT NULL
);
CREATE INDEX ix_stock_movement_format ON stock_movement (book_format_id, at DESC);

-- 3.3 Sponsorship offer on a course or book (RUL-06) — at most one active per product
CREATE TABLE sponsorship_offer (
    id                 uuid PRIMARY KEY DEFAULT uuidv7(),
    product_id         uuid NOT NULL REFERENCES product(id) ON DELETE CASCADE,
    seat_price         numeric(12,2) NOT NULL CHECK (seat_price > 0),
    unit_label_ar      text NOT NULL DEFAULT 'مقعد',
    unit_label_en      text NOT NULL DEFAULT 'seat',
    target_units       int  NOT NULL CHECK (target_units > 0),
    beneficiary_group  text NOT NULL,       -- machine key used by allocation matching (RUL-47b)
    group_label_ar     text NOT NULL,
    group_label_en     text,
    allow_partial      boolean NOT NULL DEFAULT true,
    zakat_eligible     boolean NOT NULL DEFAULT false,   -- S2
    close_on_target    boolean NOT NULL DEFAULT false,
    starts_at          timestamptz NOT NULL DEFAULT now(),
    ends_at            timestamptz NOT NULL,
    is_active          boolean NOT NULL DEFAULT true,
    created_at         timestamptz NOT NULL DEFAULT now(),
    CHECK (ends_at > starts_at)
);
CREATE UNIQUE INDEX ux_offer_one_active ON sponsorship_offer (product_id) WHERE is_active;

-- 3.4 Cause (donation product) details (RUL-07)
CREATE TABLE cause_detail (
    product_id           uuid PRIMARY KEY REFERENCES product(id) ON DELETE CASCADE,
    unit_amount          numeric(12,2) CHECK (unit_amount IS NULL OR unit_amount > 0),
    unit_label_ar        text,
    unit_label_en        text,
    target_amount        numeric(14,2) NOT NULL CHECK (target_amount > 0),
    allow_custom_amount  boolean NOT NULL DEFAULT true,
    min_amount           numeric(12,2) NOT NULL DEFAULT 10 CHECK (min_amount > 0),
    zakat_eligible       boolean NOT NULL DEFAULT false,
    beneficiaries_ar     text, beneficiaries_en text,
    close_on_target      boolean NOT NULL DEFAULT false,
    is_general_fund      boolean NOT NULL DEFAULT false,   -- receives pool remainders (RUL-46)
    starts_at            timestamptz NOT NULL DEFAULT now(),
    ends_at              timestamptz,                      -- NULL = open-ended (general fund)
    CONSTRAINT ck_cause_mode CHECK (unit_amount IS NOT NULL OR allow_custom_amount),
    CONSTRAINT ck_cause_label CHECK (unit_amount IS NULL OR unit_label_ar IS NOT NULL)
);
CREATE UNIQUE INDEX ux_one_general_fund ON cause_detail ((true)) WHERE is_general_fund;

-- 3.5 Goal counters (RUL-08) — maintained in the payment transaction, reconciled nightly
CREATE TABLE goal_progress (
    product_id      uuid PRIMARY KEY REFERENCES product(id) ON DELETE CASCADE,
    raised_amount   numeric(14,2) NOT NULL DEFAULT 0,   -- causes, and money for sponsorships
    funded_units    int NOT NULL DEFAULT 0,             -- sponsorship units created
    donor_count     int NOT NULL DEFAULT 0,             -- distinct paid orders
    zakat_amount    numeric(14,2) NOT NULL DEFAULT 0,
    seed_raised     numeric(14,2) NOT NULL DEFAULT 0,   -- opening balance migrated from offline giving
    seed_units      int NOT NULL DEFAULT 0,
    seed_donors     int NOT NULL DEFAULT 0,
    last_gift_at    timestamptz,
    updated_at      timestamptz NOT NULL DEFAULT now()
);

-- Non-order movements that change goal counters (RUL-46 pool sweep, finance corrections)
CREATE TABLE goal_adjustment (
    id                    bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    product_id            uuid NOT NULL REFERENCES product(id),
    kind                  text NOT NULL CHECK (kind IN ('pool_sweep_out','pool_sweep_in','correction')),
    amount                numeric(12,2) NOT NULL,          -- signed: negative for sweep_out
    units                 int NOT NULL DEFAULT 0,
    sponsorship_offer_id  uuid REFERENCES sponsorship_offer(id),
    pair_id               uuid,                            -- links the out/in rows of one sweep
    note                  text,
    at                    timestamptz NOT NULL DEFAULT now(),
    actor                 text NOT NULL,
    CHECK (amount <> 0 OR units <> 0)
);
CREATE INDEX ix_goal_adjustment_product ON goal_adjustment (product_id);

-- 3.6 Cause delivery updates (RUL-54)
CREATE TABLE cause_update (
    id               uuid PRIMARY KEY DEFAULT uuidv7(),
    product_id       uuid NOT NULL REFERENCES product(id) ON DELETE CASCADE,
    title_ar         text NOT NULL, title_en text,
    body_ar          text, body_en text,
    asset_id         uuid REFERENCES media_asset(id),
    units_delivered  int CHECK (units_delivered >= 0),
    occurred_on      date NOT NULL,
    is_public        boolean NOT NULL DEFAULT true,
    notify_donors    boolean NOT NULL DEFAULT false,
    notified_at      timestamptz,
    created_at       timestamptz NOT NULL DEFAULT now(),
    created_by       text NOT NULL
);
CREATE INDEX ix_cause_update_product ON cause_update (product_id, occurred_on DESC);

-- ============================================================================
--  4. IDENTITY — customers & staff (RUL-60 … RUL-62, ADR-S04)
-- ============================================================================
CREATE TABLE customer (
    id                   uuid PRIMARY KEY DEFAULT uuidv7(),
    mobile               text NOT NULL UNIQUE CHECK (mobile ~ '^\+9665[0-9]{8}$'),   -- E.164
    full_name            text,
    email                citext,
    locale               char(2) NOT NULL DEFAULT 'ar' CHECK (locale IN ('ar','en')),
    anonymous_default    boolean NOT NULL DEFAULT false,
    marketing_opt_in     boolean NOT NULL DEFAULT false,
    status               text NOT NULL DEFAULT 'active' CHECK (status IN ('active','blocked','anonymised')),
    pdpl_consent_at      timestamptz,
    last_login_at        timestamptz,
    created_at           timestamptz NOT NULL DEFAULT now(),
    updated_at           timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_customer_email ON customer (email);

CREATE TABLE customer_address (
    id                uuid PRIMARY KEY DEFAULT uuidv7(),
    customer_id       uuid NOT NULL REFERENCES customer(id) ON DELETE CASCADE,
    recipient_name    text NOT NULL,
    recipient_mobile  text NOT NULL CHECK (recipient_mobile ~ '^\+9665[0-9]{8}$'),
    city              text NOT NULL,
    district          text,
    short_address     text CHECK (short_address ~ '^[A-Z]{4}[0-9]{4}$'),   -- Saudi National Address
    street            text,
    building_no       text,
    postal_code       text CHECK (postal_code ~ '^[0-9]{5}$'),
    is_default        boolean NOT NULL DEFAULT false,
    created_at        timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_address_default ON customer_address (customer_id) WHERE is_default;

CREATE TABLE otp_challenge (
    id            uuid PRIMARY KEY DEFAULT uuidv7(),
    mobile        text NOT NULL,
    purpose       text NOT NULL CHECK (purpose IN ('login','beneficiary_invite')),
    code_hash     char(64) NOT NULL,          -- HMAC-SHA256(code, server pepper)
    attempts      smallint NOT NULL DEFAULT 0,
    expires_at    timestamptz NOT NULL,
    consumed_at   timestamptz,
    ip_hash       char(64),
    created_at    timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_otp_mobile_recent ON otp_challenge (mobile, created_at DESC);

CREATE TABLE staff_user (
    id               uuid PRIMARY KEY DEFAULT uuidv7(),
    email            citext NOT NULL UNIQUE,
    name             text NOT NULL,
    pass_hash        text NOT NULL,                 -- ASP.NET Core PasswordHasher v3 (PBKDF2) or Argon2id
    totp_secret_enc  bytea,                         -- Data Protection-encrypted; NULL until enrolled
    roles            text[] NOT NULL DEFAULT '{viewer}'
                     CHECK (roles <@ ARRAY['admin','store_manager','finance','allocation_officer','fulfilment','viewer']::text[]),
    failed_count     int NOT NULL DEFAULT 0,
    locked_until     timestamptz,
    is_active        boolean NOT NULL DEFAULT true,
    last_login_at    timestamptz,
    created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE refresh_token (
    id            uuid PRIMARY KEY DEFAULT uuidv7(),
    subject_type  text NOT NULL CHECK (subject_type IN ('customer','staff')),
    subject_id    uuid NOT NULL,
    token_hash    char(64) NOT NULL UNIQUE,         -- SHA-256; raw token only in the cookie
    family_id     uuid NOT NULL,                    -- reuse detection revokes the family
    expires_at    timestamptz NOT NULL,
    revoked_at    timestamptz,
    replaced_by   uuid,
    created_at    timestamptz NOT NULL DEFAULT now(),
    user_agent    text
);
CREATE INDEX ix_refresh_subject ON refresh_token (subject_type, subject_id);

-- ============================================================================
--  5. CART (RUL-11 … RUL-18)
-- ============================================================================
CREATE TABLE cart (
    id            uuid PRIMARY KEY DEFAULT uuidv7(),   -- opaque id in the HttpOnly cookie
    customer_id   uuid REFERENCES customer(id) ON DELETE CASCADE,
    status        text NOT NULL DEFAULT 'active' CHECK (status IN ('active','converted','merged','expired')),
    expires_at    timestamptz,                         -- guests only (RUL-16)
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_cart_customer_active ON cart (customer_id) WHERE status = 'active' AND customer_id IS NOT NULL;

CREATE TABLE cart_line (
    id               uuid PRIMARY KEY DEFAULT uuidv7(),
    cart_id          uuid NOT NULL REFERENCES cart(id) ON DELETE CASCADE,
    product_id       uuid NOT NULL REFERENCES product(id),
    book_format_id   uuid REFERENCES book_format(id),
    kind             text NOT NULL CHECK (kind IN ('buy','sponsor','donate')),
    mode             text NOT NULL CHECK (mode IN ('units','amount')),
    quantity         int  NOT NULL DEFAULT 1 CHECK (quantity BETWEEN 1 AND 999),
    unit_price       numeric(12,2) NOT NULL CHECK (unit_price >= 0),   -- indicative (RUL-15)
    amount           numeric(12,2) NOT NULL CHECK (amount > 0),
    intent           text CHECK (intent IN ('sadaqa','zakat')),
    dedication       jsonb,          -- {toName,toMobile,fromName,templateKey,text}; validated in app
    added_at         timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_cart_buy_units   CHECK (kind <> 'buy' OR mode = 'units'),
    CONSTRAINT ck_cart_buy_nointent CHECK (kind <> 'buy' OR (intent IS NULL AND dedication IS NULL)),
    CONSTRAINT ck_cart_amount_qty  CHECK (mode <> 'amount' OR quantity = 1)
);
CREATE INDEX ix_cart_line_cart ON cart_line (cart_id);

-- ============================================================================
--  6. ORDERING (RUL-24 … RUL-31)
-- ============================================================================
CREATE TABLE orders (
    id                  uuid PRIMARY KEY DEFAULT uuidv7(),
    reference           text NOT NULL UNIQUE,                 -- T3-26-001234 (RUL-28)
    customer_id         uuid NOT NULL REFERENCES customer(id),
    status              text NOT NULL DEFAULT 'pending_payment' CHECK (status IN
                          ('pending_payment','paid','payment_failed','payment_review','expired',
                           'cancelled','fulfilled','partially_refunded','refunded')),
    currency            char(3) NOT NULL DEFAULT 'SAR' CHECK (currency = 'SAR'),
    purchases_total     numeric(12,2) NOT NULL DEFAULT 0,
    shipping_total      numeric(12,2) NOT NULL DEFAULT 0,
    gifts_total         numeric(12,2) NOT NULL DEFAULT 0,
    vat_total           numeric(12,2) NOT NULL DEFAULT 0,     -- informational: included in purchases/shipping
    grand_total         numeric(12,2) NOT NULL CHECK (grand_total > 0),
    refunded_total      numeric(12,2) NOT NULL DEFAULT 0,
    is_anonymous        boolean NOT NULL DEFAULT false,
    locale              char(2) NOT NULL DEFAULT 'ar',
    contact_name        text NOT NULL,
    contact_mobile      text NOT NULL,
    contact_email       citext NOT NULL,
    shipping_address    jsonb,                                -- snapshot; required if a print line exists
    source_cart_id      uuid,
    terms_accepted_at   timestamptz NOT NULL,             -- FR-S30, E-Commerce Law evidence
    placed_at           timestamptz NOT NULL DEFAULT now(),
    payment_due_at      timestamptz NOT NULL,                 -- placed_at + timeout (RUL-29)
    paid_at             timestamptz,
    fulfilled_at        timestamptz,
    cancelled_at        timestamptz,
    CONSTRAINT ck_order_total CHECK (grand_total = purchases_total + shipping_total + gifts_total)
);
CREATE INDEX ix_order_customer ON orders (customer_id, placed_at DESC);
CREATE INDEX ix_order_status   ON orders (status, placed_at);
CREATE INDEX ix_order_due      ON orders (payment_due_at) WHERE status IN ('pending_payment','payment_failed');

CREATE TABLE order_line (
    id                  uuid PRIMARY KEY DEFAULT uuidv7(),
    order_id            uuid NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    line_no             smallint NOT NULL,
    product_id          uuid NOT NULL REFERENCES product(id),
    product_type        text NOT NULL CHECK (product_type IN ('course','book','cause')),
    book_format_id      uuid REFERENCES book_format(id),
    sponsorship_offer_id uuid REFERENCES sponsorship_offer(id),
    kind                text NOT NULL CHECK (kind IN ('buy','sponsor','donate')),
    mode                text NOT NULL CHECK (mode IN ('units','amount')),
    quantity            int  NOT NULL CHECK (quantity BETWEEN 1 AND 999),
    unit_price          numeric(12,2) NOT NULL,
    amount              numeric(12,2) NOT NULL CHECK (amount > 0),
    vat_rate            numeric(5,2)  NOT NULL DEFAULT 0,
    vat_amount          numeric(12,2) NOT NULL DEFAULT 0,
    intent              text CHECK (intent IN ('sadaqa','zakat')),
    title_ar            text NOT NULL,          -- snapshot at order time
    title_en            text,
    fulfilment_status   text NOT NULL DEFAULT 'pending' CHECK (fulfilment_status IN
                          ('pending','enrolled','pending_sync','available','preparing','shipped','delivered',
                           'returned','funded','received','refunded')),
    refunded_amount     numeric(12,2) NOT NULL DEFAULT 0,
    UNIQUE (order_id, line_no),
    CONSTRAINT ck_line_buy_vat  CHECK (kind = 'buy' OR vat_amount = 0),
    CONSTRAINT ck_line_intent   CHECK (kind = 'donate' OR kind = 'sponsor' OR intent IS NULL),
    CONSTRAINT ck_line_offer    CHECK (kind <> 'sponsor' OR sponsorship_offer_id IS NOT NULL),
    CONSTRAINT ck_line_refund   CHECK (refunded_amount <= amount)
);
CREATE INDEX ix_order_line_product ON order_line (product_id, kind);

CREATE TABLE dedication (
    id              uuid PRIMARY KEY DEFAULT uuidv7(),
    order_line_id   uuid NOT NULL UNIQUE REFERENCES order_line(id) ON DELETE CASCADE,
    to_name         text NOT NULL CHECK (length(to_name) BETWEEN 2 AND 60),
    to_mobile       text NOT NULL CHECK (to_mobile ~ '^\+9665[0-9]{8}$'),
    from_name       text,
    template_key    text NOT NULL,
    custom_text     text CHECK (length(custom_text) <= 140),
    card_token      text NOT NULL UNIQUE,            -- 22-char url-safe random, public card URL
    sms_status      text NOT NULL DEFAULT 'pending' CHECK (sms_status IN ('pending','sent','failed','skipped')),
    sent_at         timestamptz
);

-- ============================================================================
--  7. PAYMENTS (RUL-32 … RUL-38, ADR-S05)
-- ============================================================================
CREATE TABLE payment (
    id                    uuid PRIMARY KEY DEFAULT uuidv7(),
    order_id              uuid NOT NULL REFERENCES orders(id),
    provider              text NOT NULL,                  -- moyasar | hyperpay | tap … (Q-01)
    provider_payment_id   text,
    method                text CHECK (method IN ('mada','applepay','card','stcpay','bnpl')),
    amount                numeric(12,2) NOT NULL CHECK (amount > 0),
    status                text NOT NULL DEFAULT 'initiated' CHECK (status IN
                            ('initiated','authorized','captured','failed','voided','refunded','partially_refunded')),
    failure_code          text,
    failure_message       text,
    created_at            timestamptz NOT NULL DEFAULT now(),
    updated_at            timestamptz NOT NULL DEFAULT now(),
    UNIQUE (provider, provider_payment_id)
);
CREATE INDEX ix_payment_order ON payment (order_id);
CREATE INDEX ix_payment_reconcile ON payment (status, created_at) WHERE status = 'initiated';

-- Webhook inbox: every event stored before processing (idempotency, RUL-34)
CREATE TABLE payment_event (
    id                 bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    provider           text NOT NULL,
    provider_event_id  text NOT NULL,
    event_type         text NOT NULL,
    signature_valid    boolean NOT NULL,
    payload            jsonb NOT NULL,
    received_at        timestamptz NOT NULL DEFAULT now(),
    processed_at       timestamptz,
    error              text,
    UNIQUE (provider, provider_event_id)
);

CREATE TABLE refund (
    id                  uuid PRIMARY KEY DEFAULT uuidv7(),
    order_id            uuid NOT NULL REFERENCES orders(id),
    payment_id          uuid NOT NULL REFERENCES payment(id),
    amount              numeric(12,2) NOT NULL CHECK (amount > 0),
    reason              text NOT NULL,
    status              text NOT NULL DEFAULT 'requested' CHECK (status IN ('requested','succeeded','failed')),
    provider_refund_id  text,
    requested_by        text NOT NULL,              -- staff email (finance role)
    created_at          timestamptz NOT NULL DEFAULT now(),
    completed_at        timestamptz
);
CREATE TABLE refund_line (
    refund_id       uuid NOT NULL REFERENCES refund(id) ON DELETE CASCADE,
    order_line_id   uuid NOT NULL REFERENCES order_line(id),
    amount          numeric(12,2) NOT NULL CHECK (amount > 0),
    units           int,                              -- sponsored units cancelled, if any
    PRIMARY KEY (refund_id, order_line_id)
);

-- ============================================================================
--  8. DOCUMENTS (RUL-59, BR-S13)
-- ============================================================================
CREATE TABLE invoice (
    id                uuid PRIMARY KEY DEFAULT uuidv7(),
    order_id          uuid NOT NULL REFERENCES orders(id),
    kind              text NOT NULL CHECK (kind IN ('invoice','credit_note')),
    number            text NOT NULL UNIQUE,            -- INV-2026-000001 / CN-2026-000001
    issued_at         timestamptz NOT NULL DEFAULT now(),
    is_tax_invoice    boolean NOT NULL,                -- vat_enabled at issue time
    seller_vat_no     text,
    net_total         numeric(12,2) NOT NULL,
    vat_total         numeric(12,2) NOT NULL,
    gross_total       numeric(12,2) NOT NULL CHECK (gross_total = net_total + vat_total),
    references_invoice_id uuid REFERENCES invoice(id), -- credit note → original
    pdf_asset_id      uuid REFERENCES media_asset(id),
    -- ZATCA Phase 2 (S2) — populated by the e-invoicing adapter
    zatca_uuid        uuid,
    zatca_icv         bigint,                          -- invoice counter value
    zatca_pih         text,                            -- previous invoice hash
    zatca_hash        text,
    zatca_qr          text,                            -- TLV base64
    zatca_status      text CHECK (zatca_status IN ('not_required','pending','reported','cleared','rejected'))
);
CREATE INDEX ix_invoice_order ON invoice (order_id);

CREATE TABLE donation_receipt (
    id              uuid PRIMARY KEY DEFAULT uuidv7(),
    order_id        uuid NOT NULL REFERENCES orders(id),
    kind            text NOT NULL DEFAULT 'receipt' CHECK (kind IN ('receipt','cancellation')),
    number          text NOT NULL UNIQUE,              -- DR-2026-000001 / DRC-…
    issued_at       timestamptz NOT NULL DEFAULT now(),
    donor_name      text NOT NULL,                     -- real name even when anonymous (RUL-23)
    total_amount    numeric(12,2) NOT NULL,
    zakat_amount    numeric(12,2) NOT NULL DEFAULT 0,
    references_receipt_id uuid REFERENCES donation_receipt(id),
    pdf_asset_id    uuid REFERENCES media_asset(id)
);
CREATE INDEX ix_receipt_order ON donation_receipt (order_id);

-- ============================================================================
--  9. FULFILMENT (RUL-39 … RUL-42)
-- ============================================================================
CREATE TABLE enrolment (
    id               uuid PRIMARY KEY DEFAULT uuidv7(),
    customer_id      uuid NOT NULL REFERENCES customer(id),
    product_id       uuid NOT NULL REFERENCES product(id),
    source           text NOT NULL CHECK (source IN ('purchase','sponsored','manual')),
    order_line_id    uuid REFERENCES order_line(id),          -- purchase
    sponsored_unit_id uuid,                                    -- sponsored (FK added below)
    status           text NOT NULL DEFAULT 'active' CHECK (status IN ('pending_sync','active','completed','revoked','expired')),
    progress_pct     smallint NOT NULL DEFAULT 0 CHECK (progress_pct BETWEEN 0 AND 100),
    lms_enrolment_ref text,
    granted_at       timestamptz NOT NULL DEFAULT now(),
    expires_at       timestamptz,
    started_at       timestamptz,
    completed_at     timestamptz,
    certificate_no   text UNIQUE,
    CONSTRAINT ck_enrol_source CHECK (
        (source = 'purchase'  AND order_line_id IS NOT NULL) OR
        (source = 'sponsored' AND sponsored_unit_id IS NOT NULL) OR
        (source = 'manual'))
);
-- RUL-13 / RUL-47c: one live enrolment per customer per course
CREATE UNIQUE INDEX ux_enrolment_live ON enrolment (customer_id, product_id)
    WHERE status IN ('pending_sync','active','completed');

CREATE TABLE download_grant (
    id              uuid PRIMARY KEY DEFAULT uuidv7(),
    customer_id     uuid NOT NULL REFERENCES customer(id),
    order_line_id   uuid NOT NULL UNIQUE REFERENCES order_line(id),
    book_format_id  uuid NOT NULL REFERENCES book_format(id),
    max_downloads   int NOT NULL DEFAULT 5,
    download_count  int NOT NULL DEFAULT 0,
    revoked_at      timestamptz,
    created_at      timestamptz NOT NULL DEFAULT now(),
    CHECK (download_count <= max_downloads)
);
CREATE TABLE download_log (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    grant_id    uuid NOT NULL REFERENCES download_grant(id),
    at          timestamptz NOT NULL DEFAULT now(),
    ip_hash     char(64)
);

CREATE TABLE shipment (
    id              uuid PRIMARY KEY DEFAULT uuidv7(),
    order_id        uuid NOT NULL UNIQUE REFERENCES orders(id),
    status          text NOT NULL DEFAULT 'preparing' CHECK (status IN ('preparing','shipped','delivered','returned')),
    carrier         text,
    tracking_no     text,
    address         jsonb NOT NULL,
    shipped_at      timestamptz,
    delivered_at    timestamptz,
    updated_by      text
);

-- ============================================================================
--  10. GIVING — sponsorship units, pool, beneficiaries, allocation (RUL-43 … RUL-52)
-- ============================================================================
CREATE TABLE beneficiary (
    id                  uuid PRIMARY KEY DEFAULT uuidv7(),
    code                text NOT NULL UNIQUE,                 -- B-000123
    kind                text NOT NULL CHECK (kind IN ('student','teacher','parent','school','library')),
    beneficiary_group   text NOT NULL,                        -- matches sponsorship_offer.beneficiary_group
    region              text NOT NULL,                        -- e.g. القصيم (shown to donors)
    full_name_enc       bytea NOT NULL,                       -- Data Protection-encrypted PII (PDPL)
    mobile_enc          bytea,
    mobile_hash         char(64),                             -- lookup without decrypting
    guardian_name_enc   bytea,
    organisation_name   text,                                 -- school / library / home (not personal)
    eligibility_status  text NOT NULL DEFAULT 'pending' CHECK (eligibility_status IN ('pending','approved','rejected','inactive')),
    zakat_eligible      boolean NOT NULL DEFAULT false,
    eligibility_notes   text,
    verified_by         text,
    verified_at         timestamptz,
    customer_id         uuid REFERENCES customer(id),         -- linked on first OTP login (RUL-52)
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          text NOT NULL
);
CREATE INDEX ix_beneficiary_pick ON beneficiary (beneficiary_group, eligibility_status, zakat_eligible);
CREATE UNIQUE INDEX ux_beneficiary_mobile ON beneficiary (mobile_hash) WHERE mobile_hash IS NOT NULL;

CREATE TABLE allocation_batch (
    id              uuid PRIMARY KEY DEFAULT uuidv7(),
    product_id      uuid NOT NULL REFERENCES product(id),
    note            text,
    created_by      text NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE sponsored_unit (
    id                    uuid PRIMARY KEY DEFAULT uuidv7(),
    product_id            uuid NOT NULL REFERENCES product(id),
    sponsorship_offer_id  uuid NOT NULL REFERENCES sponsorship_offer(id),
    source                text NOT NULL CHECK (source IN ('full','pooled')),
    order_line_id         uuid REFERENCES order_line(id),     -- full units: the funding line
    unit_price            numeric(12,2) NOT NULL,
    has_zakat             boolean NOT NULL DEFAULT false,     -- RUL-47d
    status                text NOT NULL DEFAULT 'awaiting_allocation' CHECK (status IN
                            ('awaiting_allocation','allocated','completed','lapsed','cancelled')),
    beneficiary_id        uuid REFERENCES beneficiary(id),
    allocation_batch_id   uuid REFERENCES allocation_batch(id),
    funded_at             timestamptz NOT NULL DEFAULT now(),
    allocated_at          timestamptz,
    allocated_by          text,
    completed_at          timestamptz,
    lapsed_at             timestamptz,
    CONSTRAINT ck_unit_source  CHECK ((source = 'full') = (order_line_id IS NOT NULL)),
    CONSTRAINT ck_unit_alloc   CHECK (status NOT IN ('allocated','completed') OR beneficiary_id IS NOT NULL)
);
CREATE INDEX ix_unit_fifo       ON sponsored_unit (product_id, funded_at) WHERE status = 'awaiting_allocation';
CREATE INDEX ix_unit_orderline  ON sponsored_unit (order_line_id);
CREATE INDEX ix_unit_beneficiary ON sponsored_unit (beneficiary_id);

ALTER TABLE enrolment ADD CONSTRAINT fk_enrolment_unit
    FOREIGN KEY (sponsored_unit_id) REFERENCES sponsored_unit(id);

-- Status history for donors' timeline (RUL-50)
CREATE TABLE sponsored_unit_event (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    unit_id     uuid NOT NULL REFERENCES sponsored_unit(id) ON DELETE CASCADE,
    from_status text,
    to_status   text NOT NULL,
    at          timestamptz NOT NULL DEFAULT now(),
    actor       text NOT NULL
);
CREATE INDEX ix_unit_event_unit ON sponsored_unit_event (unit_id, at);

-- Pool (RUL-45/46): one balance row per offer
CREATE TABLE sponsorship_pool (
    sponsorship_offer_id uuid PRIMARY KEY REFERENCES sponsorship_offer(id),
    balance              numeric(12,2) NOT NULL DEFAULT 0 CHECK (balance >= 0),
    updated_at           timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE pool_contribution (
    id                    uuid PRIMARY KEY DEFAULT uuidv7(),
    sponsorship_offer_id  uuid NOT NULL REFERENCES sponsorship_offer(id),
    order_line_id         uuid NOT NULL UNIQUE REFERENCES order_line(id),
    amount                numeric(12,2) NOT NULL CHECK (amount > 0),
    remaining             numeric(12,2) NOT NULL CHECK (remaining >= 0),   -- not yet converted into units
    is_zakat              boolean NOT NULL DEFAULT false,
    swept_to_fund_at      timestamptz,                                     -- RUL-46
    created_at            timestamptz NOT NULL DEFAULT now(),
    CHECK (remaining <= amount)
);
CREATE INDEX ix_pool_fifo ON pool_contribution (sponsorship_offer_id, created_at) WHERE remaining > 0;

CREATE TABLE unit_funding (
    sponsored_unit_id     uuid NOT NULL REFERENCES sponsored_unit(id),
    pool_contribution_id  uuid NOT NULL REFERENCES pool_contribution(id),
    amount                numeric(12,2) NOT NULL CHECK (amount > 0),
    PRIMARY KEY (sponsored_unit_id, pool_contribution_id)
);

-- Book copies handed to a beneficiary organisation (RUL-47, book offers)
CREATE TABLE distribution (
    id                 uuid PRIMARY KEY DEFAULT uuidv7(),
    sponsored_unit_id  uuid NOT NULL UNIQUE REFERENCES sponsored_unit(id),
    book_format_id     uuid NOT NULL REFERENCES book_format(id),
    delivered_on       date,
    proof_asset_id     uuid REFERENCES media_asset(id)
);

-- Zakat ledger (RUL-20): every zakat in/out movement
CREATE TABLE zakat_ledger (
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    product_id      uuid NOT NULL REFERENCES product(id),
    direction       text NOT NULL CHECK (direction IN ('in','out','refund')),
    amount          numeric(12,2) NOT NULL CHECK (amount > 0),
    order_line_id   uuid REFERENCES order_line(id),
    reference       text,                  -- disbursement voucher / unit id
    at              timestamptz NOT NULL DEFAULT now(),
    actor           text NOT NULL
);
CREATE INDEX ix_zakat_product ON zakat_ledger (product_id, at);

-- ============================================================================
--  11. NOTIFICATIONS — transactional outbox (RUL-63)
-- ============================================================================
CREATE TABLE outbox_message (
    id            uuid PRIMARY KEY DEFAULT uuidv7(),
    type          text NOT NULL,             -- OrderPaid, DedicationCard, UnitAllocated …
    payload       jsonb NOT NULL,
    occurred_at   timestamptz NOT NULL DEFAULT now(),
    processed_at  timestamptz,
    attempts      int NOT NULL DEFAULT 0,
    next_attempt_at timestamptz NOT NULL DEFAULT now(),
    last_error    text
);
CREATE INDEX ix_outbox_pending ON outbox_message (next_attempt_at) WHERE processed_at IS NULL;

CREATE TABLE message_template (
    key         text NOT NULL,
    channel     text NOT NULL CHECK (channel IN ('sms','email')),
    locale      char(2) NOT NULL CHECK (locale IN ('ar','en')),
    subject     text,
    body        text NOT NULL,             -- Scriban/Liquid template
    updated_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (key, channel, locale)
);

CREATE TABLE notification (
    id                uuid PRIMARY KEY DEFAULT uuidv7(),
    channel           text NOT NULL CHECK (channel IN ('sms','email')),
    recipient         text NOT NULL,
    template_key      text NOT NULL,
    locale            char(2) NOT NULL,
    status            text NOT NULL DEFAULT 'queued' CHECK (status IN ('queued','sent','failed')),
    provider_msg_id   text,
    error             text,
    related_order_id  uuid REFERENCES orders(id),
    outbox_message_id uuid REFERENCES outbox_message(id),   -- dedupe: a retried outbox message never re-sends
    created_at        timestamptz NOT NULL DEFAULT now(),
    sent_at           timestamptz
);
CREATE UNIQUE INDEX ux_notification_once ON notification (outbox_message_id, channel, recipient)
    WHERE outbox_message_id IS NOT NULL;
CREATE INDEX ix_notification_order ON notification (related_order_id);

-- Idempotency-Key store for POST /v1/checkout and admin refunds (API contract §1)
CREATE TABLE idempotency_key (
    key            text NOT NULL,
    scope          text NOT NULL,                 -- e.g. checkout:{customerId}, refund:{staffId}
    request_hash   char(64) NOT NULL,             -- SHA-256 of the canonical request body
    status_code    int,
    response       jsonb,
    created_at     timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (scope, key)
);
CREATE INDEX ix_idempotency_created ON idempotency_key (created_at);   -- pruned after 24 h

-- ============================================================================
--  12. REPORTING VIEWS (FR-S70 …)
-- ============================================================================
CREATE VIEW v_goal AS
SELECT p.id AS product_id, p.slug, p.type,
       coalesce(c.target_amount, NULL)                       AS target_amount,
       so.target_units,
       g.raised_amount + g.seed_raised                       AS raised_amount,   -- raised_amount already includes goal_adjustment
       g.funded_units  + g.seed_units                        AS funded_units,
       g.donor_count   + g.seed_donors                       AS donor_count,
       coalesce(c.ends_at, so.ends_at)                       AS ends_at
FROM product p
LEFT JOIN goal_progress g     ON g.product_id = p.id
LEFT JOIN cause_detail c      ON c.product_id = p.id
LEFT JOIN sponsorship_offer so ON so.product_id = p.id AND so.is_active;

CREATE VIEW v_sales_daily AS
SELECT (o.paid_at AT TIME ZONE 'Asia/Riyadh')::date AS day,
       l.kind, l.product_type,
       count(DISTINCT o.id) AS orders,
       sum(l.amount)        AS gross,
       sum(l.vat_amount)    AS vat,
       sum(l.refunded_amount) AS refunded
FROM orders o JOIN order_line l ON l.order_id = o.id
WHERE o.paid_at IS NOT NULL
GROUP BY 1,2,3;

CREATE VIEW v_allocation_backlog AS
SELECT u.product_id, p.slug,
       count(*) FILTER (WHERE u.status = 'awaiting_allocation')                              AS awaiting,
       count(*) FILTER (WHERE u.status = 'awaiting_allocation' AND u.funded_at < now() - interval '30 days') AS overdue_30,
       count(*) FILTER (WHERE u.status = 'allocated')                                        AS allocated,
       count(*) FILTER (WHERE u.status = 'completed')                                        AS completed,
       min(u.funded_at) FILTER (WHERE u.status = 'awaiting_allocation')                      AS oldest_awaiting
FROM sponsored_unit u JOIN product p ON p.id = u.product_id
GROUP BY u.product_id, p.slug;

CREATE VIEW v_zakat_balance AS
SELECT product_id,
       sum(amount) FILTER (WHERE direction = 'in')     AS received,
       sum(amount) FILTER (WHERE direction = 'out')    AS disbursed,
       sum(amount) FILTER (WHERE direction = 'refund') AS refunded,
       coalesce(sum(amount) FILTER (WHERE direction = 'in'),0)
     - coalesce(sum(amount) FILTER (WHERE direction IN ('out','refund')),0) AS balance
FROM zakat_ledger GROUP BY product_id;
