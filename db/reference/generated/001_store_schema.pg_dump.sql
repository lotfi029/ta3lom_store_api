--
-- PostgreSQL database dump
--


-- Dumped from database version 16.13 (Ubuntu 16.13-0ubuntu0.24.04.1)
-- Dumped by pg_dump version 16.13 (Ubuntu 16.13-0ubuntu0.24.04.1)

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: store; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA store;


--
-- Name: ar_normalize(text); Type: FUNCTION; Schema: store; Owner: -
--

CREATE FUNCTION store.ar_normalize(t text) RETURNS text
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
    AS $$
      SELECT lower(translate(
               regexp_replace(coalesce(t,''), '[\u064B-\u0652\u0640]', '', 'g'),
               'أإآٱةى', 'اااايهي'))
    $$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: allocation_batch; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.allocation_batch (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    product_id uuid NOT NULL,
    note text,
    created_by text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: audit_log; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.audit_log (
    id bigint NOT NULL,
    at timestamp with time zone DEFAULT now() NOT NULL,
    actor text NOT NULL,
    action text NOT NULL,
    entity text NOT NULL,
    entity_id text,
    diff jsonb,
    ip_hash character(64)
);


--
-- Name: audit_log_id_seq; Type: SEQUENCE; Schema: store; Owner: -
--

ALTER TABLE store.audit_log ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME store.audit_log_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: beneficiary; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.beneficiary (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    code text NOT NULL,
    kind text NOT NULL,
    beneficiary_group text NOT NULL,
    region text NOT NULL,
    full_name_enc bytea NOT NULL,
    mobile_enc bytea,
    mobile_hash character(64),
    guardian_name_enc bytea,
    organisation_name text,
    eligibility_status text DEFAULT 'pending'::text NOT NULL,
    zakat_eligible boolean DEFAULT false NOT NULL,
    eligibility_notes text,
    verified_by text,
    verified_at timestamp with time zone,
    customer_id uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by text NOT NULL,
    CONSTRAINT beneficiary_eligibility_status_check CHECK ((eligibility_status = ANY (ARRAY['pending'::text, 'approved'::text, 'rejected'::text, 'inactive'::text]))),
    CONSTRAINT beneficiary_kind_check CHECK ((kind = ANY (ARRAY['student'::text, 'teacher'::text, 'parent'::text, 'school'::text, 'library'::text])))
);


--
-- Name: beneficiary_code_seq; Type: SEQUENCE; Schema: store; Owner: -
--

CREATE SEQUENCE store.beneficiary_code_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: book_detail; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.book_detail (
    product_id uuid NOT NULL,
    author_ar text NOT NULL,
    author_en text,
    pages integer,
    isbn text,
    published_year integer,
    CONSTRAINT book_detail_pages_check CHECK ((pages > 0))
);


--
-- Name: book_format; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.book_format (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    product_id uuid NOT NULL,
    kind text NOT NULL,
    sku text NOT NULL,
    price numeric(12,2) NOT NULL,
    compare_at_price numeric(12,2),
    is_active boolean DEFAULT true NOT NULL,
    stock_on_hand integer,
    stock_reserved integer DEFAULT 0 NOT NULL,
    weight_grams integer,
    low_stock_threshold integer,
    file_asset_id uuid,
    CONSTRAINT book_format_check CHECK (((compare_at_price IS NULL) OR (compare_at_price > price))),
    CONSTRAINT book_format_kind_check CHECK ((kind = ANY (ARRAY['print'::text, 'digital'::text]))),
    CONSTRAINT book_format_low_stock_threshold_check CHECK ((low_stock_threshold >= 0)),
    CONSTRAINT book_format_price_check CHECK ((price >= (0)::numeric)),
    CONSTRAINT book_format_stock_on_hand_check CHECK ((stock_on_hand >= 0)),
    CONSTRAINT book_format_stock_reserved_check CHECK ((stock_reserved >= 0)),
    CONSTRAINT ck_format_digital CHECK (((kind <> 'digital'::text) OR (file_asset_id IS NOT NULL))),
    CONSTRAINT ck_format_print CHECK (((kind <> 'print'::text) OR (stock_on_hand IS NOT NULL))),
    CONSTRAINT ck_format_reserve CHECK ((stock_reserved <= COALESCE(stock_on_hand, 0)))
);


--
-- Name: cart; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.cart (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    customer_id uuid,
    status text DEFAULT 'active'::text NOT NULL,
    expires_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT cart_status_check CHECK ((status = ANY (ARRAY['active'::text, 'converted'::text, 'merged'::text, 'expired'::text])))
);


--
-- Name: cart_line; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.cart_line (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    cart_id uuid NOT NULL,
    product_id uuid NOT NULL,
    book_format_id uuid,
    kind text NOT NULL,
    mode text NOT NULL,
    quantity integer DEFAULT 1 NOT NULL,
    unit_price numeric(12,2) NOT NULL,
    amount numeric(12,2) NOT NULL,
    intent text,
    dedication jsonb,
    added_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT cart_line_amount_check CHECK ((amount > (0)::numeric)),
    CONSTRAINT cart_line_intent_check CHECK ((intent = ANY (ARRAY['sadaqa'::text, 'zakat'::text]))),
    CONSTRAINT cart_line_kind_check CHECK ((kind = ANY (ARRAY['buy'::text, 'sponsor'::text, 'donate'::text]))),
    CONSTRAINT cart_line_mode_check CHECK ((mode = ANY (ARRAY['units'::text, 'amount'::text]))),
    CONSTRAINT cart_line_quantity_check CHECK (((quantity >= 1) AND (quantity <= 999))),
    CONSTRAINT cart_line_unit_price_check CHECK ((unit_price >= (0)::numeric)),
    CONSTRAINT ck_cart_amount_qty CHECK (((mode <> 'amount'::text) OR (quantity = 1))),
    CONSTRAINT ck_cart_buy_nointent CHECK (((kind <> 'buy'::text) OR ((intent IS NULL) AND (dedication IS NULL)))),
    CONSTRAINT ck_cart_buy_units CHECK (((kind <> 'buy'::text) OR (mode = 'units'::text)))
);


--
-- Name: category; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.category (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    slug text NOT NULL,
    name_ar text NOT NULL,
    name_en text,
    sort integer DEFAULT 0 NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    CONSTRAINT category_slug_check CHECK ((slug ~ '^[a-z0-9-]{2,60}$'::text))
);


--
-- Name: cause_detail; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.cause_detail (
    product_id uuid NOT NULL,
    unit_amount numeric(12,2),
    unit_label_ar text,
    unit_label_en text,
    target_amount numeric(14,2) NOT NULL,
    allow_custom_amount boolean DEFAULT true NOT NULL,
    min_amount numeric(12,2) DEFAULT 10 NOT NULL,
    zakat_eligible boolean DEFAULT false NOT NULL,
    beneficiaries_ar text,
    beneficiaries_en text,
    close_on_target boolean DEFAULT false NOT NULL,
    is_general_fund boolean DEFAULT false NOT NULL,
    starts_at timestamp with time zone DEFAULT now() NOT NULL,
    ends_at timestamp with time zone,
    CONSTRAINT cause_detail_min_amount_check CHECK ((min_amount > (0)::numeric)),
    CONSTRAINT cause_detail_target_amount_check CHECK ((target_amount > (0)::numeric)),
    CONSTRAINT cause_detail_unit_amount_check CHECK (((unit_amount IS NULL) OR (unit_amount > (0)::numeric))),
    CONSTRAINT ck_cause_label CHECK (((unit_amount IS NULL) OR (unit_label_ar IS NOT NULL))),
    CONSTRAINT ck_cause_mode CHECK (((unit_amount IS NOT NULL) OR allow_custom_amount))
);


--
-- Name: cause_update; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.cause_update (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    product_id uuid NOT NULL,
    title_ar text NOT NULL,
    title_en text,
    body_ar text,
    body_en text,
    asset_id uuid,
    units_delivered integer,
    occurred_on date NOT NULL,
    is_public boolean DEFAULT true NOT NULL,
    notify_donors boolean DEFAULT false NOT NULL,
    notified_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by text NOT NULL,
    CONSTRAINT cause_update_units_delivered_check CHECK ((units_delivered >= 0))
);


--
-- Name: course_detail; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.course_detail (
    product_id uuid NOT NULL,
    price numeric(12,2) NOT NULL,
    compare_at_price numeric(12,2),
    lessons integer DEFAULT 0 NOT NULL,
    duration_minutes integer DEFAULT 0 NOT NULL,
    level_ar text,
    level_en text,
    instructor_ar text NOT NULL,
    instructor_en text,
    access_days integer,
    lms_course_ref text,
    learner_count integer DEFAULT 0 NOT NULL,
    CONSTRAINT course_detail_access_days_check CHECK (((access_days IS NULL) OR (access_days > 0))),
    CONSTRAINT course_detail_check CHECK (((compare_at_price IS NULL) OR (compare_at_price > price))),
    CONSTRAINT course_detail_duration_minutes_check CHECK ((duration_minutes >= 0)),
    CONSTRAINT course_detail_lessons_check CHECK ((lessons >= 0)),
    CONSTRAINT course_detail_price_check CHECK ((price >= (0)::numeric))
);


--
-- Name: course_module; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.course_module (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    product_id uuid NOT NULL,
    sort integer NOT NULL,
    title_ar text NOT NULL,
    title_en text,
    duration_seconds integer DEFAULT 0 NOT NULL
);


--
-- Name: customer; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.customer (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    mobile text NOT NULL,
    full_name text,
    email store.citext,
    locale character(2) DEFAULT 'ar'::bpchar NOT NULL,
    anonymous_default boolean DEFAULT false NOT NULL,
    marketing_opt_in boolean DEFAULT false NOT NULL,
    status text DEFAULT 'active'::text NOT NULL,
    pdpl_consent_at timestamp with time zone,
    last_login_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT customer_locale_check CHECK ((locale = ANY (ARRAY['ar'::bpchar, 'en'::bpchar]))),
    CONSTRAINT customer_mobile_check CHECK ((mobile ~ '^\+9665[0-9]{8}$'::text)),
    CONSTRAINT customer_status_check CHECK ((status = ANY (ARRAY['active'::text, 'blocked'::text, 'anonymised'::text])))
);


--
-- Name: customer_address; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.customer_address (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    customer_id uuid NOT NULL,
    recipient_name text NOT NULL,
    recipient_mobile text NOT NULL,
    city text NOT NULL,
    district text,
    short_address text,
    street text,
    building_no text,
    postal_code text,
    is_default boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT customer_address_postal_code_check CHECK ((postal_code ~ '^[0-9]{5}$'::text)),
    CONSTRAINT customer_address_recipient_mobile_check CHECK ((recipient_mobile ~ '^\+9665[0-9]{8}$'::text)),
    CONSTRAINT customer_address_short_address_check CHECK ((short_address ~ '^[A-Z]{4}[0-9]{4}$'::text))
);


--
-- Name: dedication; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.dedication (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    order_line_id uuid NOT NULL,
    to_name text NOT NULL,
    to_mobile text NOT NULL,
    from_name text,
    template_key text NOT NULL,
    custom_text text,
    card_token text NOT NULL,
    sms_status text DEFAULT 'pending'::text NOT NULL,
    sent_at timestamp with time zone,
    CONSTRAINT dedication_custom_text_check CHECK ((length(custom_text) <= 140)),
    CONSTRAINT dedication_sms_status_check CHECK ((sms_status = ANY (ARRAY['pending'::text, 'sent'::text, 'failed'::text, 'skipped'::text]))),
    CONSTRAINT dedication_to_mobile_check CHECK ((to_mobile ~ '^\+9665[0-9]{8}$'::text)),
    CONSTRAINT dedication_to_name_check CHECK (((length(to_name) >= 2) AND (length(to_name) <= 60)))
);


--
-- Name: distribution; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.distribution (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    sponsored_unit_id uuid NOT NULL,
    book_format_id uuid NOT NULL,
    delivered_on date,
    proof_asset_id uuid
);


--
-- Name: document_counter; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.document_counter (
    series text NOT NULL,
    year integer NOT NULL,
    last_value integer DEFAULT 0 NOT NULL,
    CONSTRAINT document_counter_series_check CHECK ((series = ANY (ARRAY['INV'::text, 'CN'::text, 'DR'::text, 'DRC'::text])))
);


--
-- Name: donation_receipt; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.donation_receipt (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    order_id uuid NOT NULL,
    kind text DEFAULT 'receipt'::text NOT NULL,
    number text NOT NULL,
    issued_at timestamp with time zone DEFAULT now() NOT NULL,
    donor_name text NOT NULL,
    total_amount numeric(12,2) NOT NULL,
    zakat_amount numeric(12,2) DEFAULT 0 NOT NULL,
    references_receipt_id uuid,
    pdf_asset_id uuid,
    CONSTRAINT donation_receipt_kind_check CHECK ((kind = ANY (ARRAY['receipt'::text, 'cancellation'::text])))
);


--
-- Name: download_grant; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.download_grant (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    customer_id uuid NOT NULL,
    order_line_id uuid NOT NULL,
    book_format_id uuid NOT NULL,
    max_downloads integer DEFAULT 5 NOT NULL,
    download_count integer DEFAULT 0 NOT NULL,
    revoked_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT download_grant_check CHECK ((download_count <= max_downloads))
);


--
-- Name: download_log; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.download_log (
    id bigint NOT NULL,
    grant_id uuid NOT NULL,
    at timestamp with time zone DEFAULT now() NOT NULL,
    ip_hash character(64)
);


--
-- Name: download_log_id_seq; Type: SEQUENCE; Schema: store; Owner: -
--

ALTER TABLE store.download_log ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME store.download_log_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: enrolment; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.enrolment (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    customer_id uuid NOT NULL,
    product_id uuid NOT NULL,
    source text NOT NULL,
    order_line_id uuid,
    sponsored_unit_id uuid,
    status text DEFAULT 'active'::text NOT NULL,
    progress_pct smallint DEFAULT 0 NOT NULL,
    lms_enrolment_ref text,
    granted_at timestamp with time zone DEFAULT now() NOT NULL,
    expires_at timestamp with time zone,
    started_at timestamp with time zone,
    completed_at timestamp with time zone,
    certificate_no text,
    CONSTRAINT ck_enrol_source CHECK ((((source = 'purchase'::text) AND (order_line_id IS NOT NULL)) OR ((source = 'sponsored'::text) AND (sponsored_unit_id IS NOT NULL)) OR (source = 'manual'::text))),
    CONSTRAINT enrolment_progress_pct_check CHECK (((progress_pct >= 0) AND (progress_pct <= 100))),
    CONSTRAINT enrolment_source_check CHECK ((source = ANY (ARRAY['purchase'::text, 'sponsored'::text, 'manual'::text]))),
    CONSTRAINT enrolment_status_check CHECK ((status = ANY (ARRAY['pending_sync'::text, 'active'::text, 'completed'::text, 'revoked'::text, 'expired'::text])))
);


--
-- Name: goal_adjustment; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.goal_adjustment (
    id bigint NOT NULL,
    product_id uuid NOT NULL,
    kind text NOT NULL,
    amount numeric(12,2) NOT NULL,
    units integer DEFAULT 0 NOT NULL,
    sponsorship_offer_id uuid,
    pair_id uuid,
    note text,
    at timestamp with time zone DEFAULT now() NOT NULL,
    actor text NOT NULL,
    CONSTRAINT goal_adjustment_check CHECK (((amount <> (0)::numeric) OR (units <> 0))),
    CONSTRAINT goal_adjustment_kind_check CHECK ((kind = ANY (ARRAY['pool_sweep_out'::text, 'pool_sweep_in'::text, 'correction'::text])))
);


--
-- Name: goal_adjustment_id_seq; Type: SEQUENCE; Schema: store; Owner: -
--

ALTER TABLE store.goal_adjustment ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME store.goal_adjustment_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: goal_progress; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.goal_progress (
    product_id uuid NOT NULL,
    raised_amount numeric(14,2) DEFAULT 0 NOT NULL,
    funded_units integer DEFAULT 0 NOT NULL,
    donor_count integer DEFAULT 0 NOT NULL,
    zakat_amount numeric(14,2) DEFAULT 0 NOT NULL,
    seed_raised numeric(14,2) DEFAULT 0 NOT NULL,
    seed_units integer DEFAULT 0 NOT NULL,
    seed_donors integer DEFAULT 0 NOT NULL,
    last_gift_at timestamp with time zone,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: idempotency_key; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.idempotency_key (
    key text NOT NULL,
    scope text NOT NULL,
    request_hash character(64) NOT NULL,
    status_code integer,
    response jsonb,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: invoice; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.invoice (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    order_id uuid NOT NULL,
    kind text NOT NULL,
    number text NOT NULL,
    issued_at timestamp with time zone DEFAULT now() NOT NULL,
    is_tax_invoice boolean NOT NULL,
    seller_vat_no text,
    net_total numeric(12,2) NOT NULL,
    vat_total numeric(12,2) NOT NULL,
    gross_total numeric(12,2) NOT NULL,
    references_invoice_id uuid,
    pdf_asset_id uuid,
    zatca_uuid uuid,
    zatca_icv bigint,
    zatca_pih text,
    zatca_hash text,
    zatca_qr text,
    zatca_status text,
    CONSTRAINT invoice_check CHECK ((gross_total = (net_total + vat_total))),
    CONSTRAINT invoice_kind_check CHECK ((kind = ANY (ARRAY['invoice'::text, 'credit_note'::text]))),
    CONSTRAINT invoice_zatca_status_check CHECK ((zatca_status = ANY (ARRAY['not_required'::text, 'pending'::text, 'reported'::text, 'cleared'::text, 'rejected'::text])))
);


--
-- Name: media_asset; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.media_asset (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    kind text NOT NULL,
    storage_key text NOT NULL,
    is_public boolean DEFAULT true NOT NULL,
    mime text NOT NULL,
    size_bytes bigint NOT NULL,
    width integer,
    height integer,
    alt_ar text,
    alt_en text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_media_alt CHECK (((kind <> 'image'::text) OR (alt_ar IS NOT NULL))),
    CONSTRAINT ck_media_private_book CHECK (((kind <> 'book_file'::text) OR (is_public = false))),
    CONSTRAINT media_asset_kind_check CHECK ((kind = ANY (ARRAY['image'::text, 'pdf'::text, 'book_file'::text]))),
    CONSTRAINT media_asset_size_bytes_check CHECK ((size_bytes > 0))
);


--
-- Name: message_template; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.message_template (
    key text NOT NULL,
    channel text NOT NULL,
    locale character(2) NOT NULL,
    subject text,
    body text NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT message_template_channel_check CHECK ((channel = ANY (ARRAY['sms'::text, 'email'::text]))),
    CONSTRAINT message_template_locale_check CHECK ((locale = ANY (ARRAY['ar'::bpchar, 'en'::bpchar])))
);


--
-- Name: notification; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.notification (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    channel text NOT NULL,
    recipient text NOT NULL,
    template_key text NOT NULL,
    locale character(2) NOT NULL,
    status text DEFAULT 'queued'::text NOT NULL,
    provider_msg_id text,
    error text,
    related_order_id uuid,
    outbox_message_id uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    sent_at timestamp with time zone,
    CONSTRAINT notification_channel_check CHECK ((channel = ANY (ARRAY['sms'::text, 'email'::text]))),
    CONSTRAINT notification_status_check CHECK ((status = ANY (ARRAY['queued'::text, 'sent'::text, 'failed'::text])))
);


--
-- Name: order_line; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.order_line (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    order_id uuid NOT NULL,
    line_no smallint NOT NULL,
    product_id uuid NOT NULL,
    product_type text NOT NULL,
    book_format_id uuid,
    sponsorship_offer_id uuid,
    kind text NOT NULL,
    mode text NOT NULL,
    quantity integer NOT NULL,
    unit_price numeric(12,2) NOT NULL,
    amount numeric(12,2) NOT NULL,
    vat_rate numeric(5,2) DEFAULT 0 NOT NULL,
    vat_amount numeric(12,2) DEFAULT 0 NOT NULL,
    intent text,
    title_ar text NOT NULL,
    title_en text,
    fulfilment_status text DEFAULT 'pending'::text NOT NULL,
    refunded_amount numeric(12,2) DEFAULT 0 NOT NULL,
    CONSTRAINT ck_line_buy_vat CHECK (((kind = 'buy'::text) OR (vat_amount = (0)::numeric))),
    CONSTRAINT ck_line_intent CHECK (((kind = 'donate'::text) OR (kind = 'sponsor'::text) OR (intent IS NULL))),
    CONSTRAINT ck_line_offer CHECK (((kind <> 'sponsor'::text) OR (sponsorship_offer_id IS NOT NULL))),
    CONSTRAINT ck_line_refund CHECK ((refunded_amount <= amount)),
    CONSTRAINT order_line_amount_check CHECK ((amount > (0)::numeric)),
    CONSTRAINT order_line_fulfilment_status_check CHECK ((fulfilment_status = ANY (ARRAY['pending'::text, 'enrolled'::text, 'pending_sync'::text, 'available'::text, 'preparing'::text, 'shipped'::text, 'delivered'::text, 'returned'::text, 'funded'::text, 'received'::text, 'refunded'::text]))),
    CONSTRAINT order_line_intent_check CHECK ((intent = ANY (ARRAY['sadaqa'::text, 'zakat'::text]))),
    CONSTRAINT order_line_kind_check CHECK ((kind = ANY (ARRAY['buy'::text, 'sponsor'::text, 'donate'::text]))),
    CONSTRAINT order_line_mode_check CHECK ((mode = ANY (ARRAY['units'::text, 'amount'::text]))),
    CONSTRAINT order_line_product_type_check CHECK ((product_type = ANY (ARRAY['course'::text, 'book'::text, 'cause'::text]))),
    CONSTRAINT order_line_quantity_check CHECK (((quantity >= 1) AND (quantity <= 999)))
);


--
-- Name: order_ref_seq; Type: SEQUENCE; Schema: store; Owner: -
--

CREATE SEQUENCE store.order_ref_seq
    START WITH 1000
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: orders; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.orders (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    reference text NOT NULL,
    customer_id uuid NOT NULL,
    status text DEFAULT 'pending_payment'::text NOT NULL,
    currency character(3) DEFAULT 'SAR'::bpchar NOT NULL,
    purchases_total numeric(12,2) DEFAULT 0 NOT NULL,
    shipping_total numeric(12,2) DEFAULT 0 NOT NULL,
    gifts_total numeric(12,2) DEFAULT 0 NOT NULL,
    vat_total numeric(12,2) DEFAULT 0 NOT NULL,
    grand_total numeric(12,2) NOT NULL,
    refunded_total numeric(12,2) DEFAULT 0 NOT NULL,
    is_anonymous boolean DEFAULT false NOT NULL,
    locale character(2) DEFAULT 'ar'::bpchar NOT NULL,
    contact_name text NOT NULL,
    contact_mobile text NOT NULL,
    contact_email store.citext NOT NULL,
    shipping_address jsonb,
    source_cart_id uuid,
    terms_accepted_at timestamp with time zone NOT NULL,
    placed_at timestamp with time zone DEFAULT now() NOT NULL,
    payment_due_at timestamp with time zone NOT NULL,
    paid_at timestamp with time zone,
    fulfilled_at timestamp with time zone,
    cancelled_at timestamp with time zone,
    CONSTRAINT ck_order_total CHECK ((grand_total = ((purchases_total + shipping_total) + gifts_total))),
    CONSTRAINT orders_currency_check CHECK ((currency = 'SAR'::bpchar)),
    CONSTRAINT orders_grand_total_check CHECK ((grand_total > (0)::numeric)),
    CONSTRAINT orders_status_check CHECK ((status = ANY (ARRAY['pending_payment'::text, 'paid'::text, 'payment_failed'::text, 'payment_review'::text, 'expired'::text, 'cancelled'::text, 'fulfilled'::text, 'partially_refunded'::text, 'refunded'::text])))
);


--
-- Name: otp_challenge; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.otp_challenge (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    mobile text NOT NULL,
    purpose text NOT NULL,
    code_hash character(64) NOT NULL,
    attempts smallint DEFAULT 0 NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    consumed_at timestamp with time zone,
    ip_hash character(64),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT otp_challenge_purpose_check CHECK ((purpose = ANY (ARRAY['login'::text, 'beneficiary_invite'::text])))
);


--
-- Name: outbox_message; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.outbox_message (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    type text NOT NULL,
    payload jsonb NOT NULL,
    occurred_at timestamp with time zone DEFAULT now() NOT NULL,
    processed_at timestamp with time zone,
    attempts integer DEFAULT 0 NOT NULL,
    next_attempt_at timestamp with time zone DEFAULT now() NOT NULL,
    last_error text
);


--
-- Name: payment; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.payment (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    order_id uuid NOT NULL,
    provider text NOT NULL,
    provider_payment_id text,
    method text,
    amount numeric(12,2) NOT NULL,
    status text DEFAULT 'initiated'::text NOT NULL,
    failure_code text,
    failure_message text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT payment_amount_check CHECK ((amount > (0)::numeric)),
    CONSTRAINT payment_method_check CHECK ((method = ANY (ARRAY['mada'::text, 'applepay'::text, 'card'::text, 'stcpay'::text, 'bnpl'::text]))),
    CONSTRAINT payment_status_check CHECK ((status = ANY (ARRAY['initiated'::text, 'authorized'::text, 'captured'::text, 'failed'::text, 'voided'::text, 'refunded'::text, 'partially_refunded'::text])))
);


--
-- Name: payment_event; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.payment_event (
    id bigint NOT NULL,
    provider text NOT NULL,
    provider_event_id text NOT NULL,
    event_type text NOT NULL,
    signature_valid boolean NOT NULL,
    payload jsonb NOT NULL,
    received_at timestamp with time zone DEFAULT now() NOT NULL,
    processed_at timestamp with time zone,
    error text
);


--
-- Name: payment_event_id_seq; Type: SEQUENCE; Schema: store; Owner: -
--

ALTER TABLE store.payment_event ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME store.payment_event_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: pool_contribution; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.pool_contribution (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    sponsorship_offer_id uuid NOT NULL,
    order_line_id uuid NOT NULL,
    amount numeric(12,2) NOT NULL,
    remaining numeric(12,2) NOT NULL,
    is_zakat boolean DEFAULT false NOT NULL,
    swept_to_fund_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT pool_contribution_amount_check CHECK ((amount > (0)::numeric)),
    CONSTRAINT pool_contribution_check CHECK ((remaining <= amount)),
    CONSTRAINT pool_contribution_remaining_check CHECK ((remaining >= (0)::numeric))
);


--
-- Name: product; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.product (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    type text NOT NULL,
    slug text NOT NULL,
    status text DEFAULT 'draft'::text NOT NULL,
    category_id uuid,
    cover_asset_id uuid,
    title_ar text NOT NULL,
    title_en text,
    summary_ar text NOT NULL,
    summary_en text,
    body_ar text,
    body_en text,
    badge text,
    sort integer DEFAULT 0 NOT NULL,
    seo_title_ar text,
    seo_title_en text,
    seo_desc_ar text,
    seo_desc_en text,
    published_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    search_vector tsvector GENERATED ALWAYS AS (to_tsvector('simple'::regconfig, store.ar_normalize(((((((COALESCE(title_ar, ''::text) || ' '::text) || COALESCE(title_en, ''::text)) || ' '::text) || COALESCE(summary_ar, ''::text)) || ' '::text) || COALESCE(summary_en, ''::text))))) STORED,
    CONSTRAINT product_badge_check CHECK ((badge = ANY (ARRAY['best'::text, 'new'::text, 'urgent'::text]))),
    CONSTRAINT product_slug_check CHECK ((slug ~ '^[a-z0-9-]{2,80}$'::text)),
    CONSTRAINT product_status_check CHECK ((status = ANY (ARRAY['draft'::text, 'published'::text, 'archived'::text]))),
    CONSTRAINT product_title_ar_check CHECK (((length(title_ar) >= 2) AND (length(title_ar) <= 160))),
    CONSTRAINT product_type_check CHECK ((type = ANY (ARRAY['course'::text, 'book'::text, 'cause'::text])))
);


--
-- Name: refresh_token; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.refresh_token (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    subject_type text NOT NULL,
    subject_id uuid NOT NULL,
    token_hash character(64) NOT NULL,
    family_id uuid NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone,
    replaced_by uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    user_agent text,
    CONSTRAINT refresh_token_subject_type_check CHECK ((subject_type = ANY (ARRAY['customer'::text, 'staff'::text])))
);


--
-- Name: refund; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.refund (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    order_id uuid NOT NULL,
    payment_id uuid NOT NULL,
    amount numeric(12,2) NOT NULL,
    reason text NOT NULL,
    status text DEFAULT 'requested'::text NOT NULL,
    provider_refund_id text,
    requested_by text NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    completed_at timestamp with time zone,
    CONSTRAINT refund_amount_check CHECK ((amount > (0)::numeric)),
    CONSTRAINT refund_status_check CHECK ((status = ANY (ARRAY['requested'::text, 'succeeded'::text, 'failed'::text])))
);


--
-- Name: refund_line; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.refund_line (
    refund_id uuid NOT NULL,
    order_line_id uuid NOT NULL,
    amount numeric(12,2) NOT NULL,
    units integer,
    CONSTRAINT refund_line_amount_check CHECK ((amount > (0)::numeric))
);


--
-- Name: setting; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.setting (
    key text NOT NULL,
    value jsonb NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_by text
);


--
-- Name: shipment; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.shipment (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    order_id uuid NOT NULL,
    status text DEFAULT 'preparing'::text NOT NULL,
    carrier text,
    tracking_no text,
    address jsonb NOT NULL,
    shipped_at timestamp with time zone,
    delivered_at timestamp with time zone,
    updated_by text,
    CONSTRAINT shipment_status_check CHECK ((status = ANY (ARRAY['preparing'::text, 'shipped'::text, 'delivered'::text, 'returned'::text])))
);


--
-- Name: sponsored_unit; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.sponsored_unit (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    product_id uuid NOT NULL,
    sponsorship_offer_id uuid NOT NULL,
    source text NOT NULL,
    order_line_id uuid,
    unit_price numeric(12,2) NOT NULL,
    has_zakat boolean DEFAULT false NOT NULL,
    status text DEFAULT 'awaiting_allocation'::text NOT NULL,
    beneficiary_id uuid,
    allocation_batch_id uuid,
    funded_at timestamp with time zone DEFAULT now() NOT NULL,
    allocated_at timestamp with time zone,
    allocated_by text,
    completed_at timestamp with time zone,
    lapsed_at timestamp with time zone,
    CONSTRAINT ck_unit_alloc CHECK (((status <> ALL (ARRAY['allocated'::text, 'completed'::text])) OR (beneficiary_id IS NOT NULL))),
    CONSTRAINT ck_unit_source CHECK (((source = 'full'::text) = (order_line_id IS NOT NULL))),
    CONSTRAINT sponsored_unit_source_check CHECK ((source = ANY (ARRAY['full'::text, 'pooled'::text]))),
    CONSTRAINT sponsored_unit_status_check CHECK ((status = ANY (ARRAY['awaiting_allocation'::text, 'allocated'::text, 'completed'::text, 'lapsed'::text, 'cancelled'::text])))
);


--
-- Name: sponsored_unit_event; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.sponsored_unit_event (
    id bigint NOT NULL,
    unit_id uuid NOT NULL,
    from_status text,
    to_status text NOT NULL,
    at timestamp with time zone DEFAULT now() NOT NULL,
    actor text NOT NULL
);


--
-- Name: sponsored_unit_event_id_seq; Type: SEQUENCE; Schema: store; Owner: -
--

ALTER TABLE store.sponsored_unit_event ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME store.sponsored_unit_event_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: sponsorship_offer; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.sponsorship_offer (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    product_id uuid NOT NULL,
    seat_price numeric(12,2) NOT NULL,
    unit_label_ar text DEFAULT 'مقعد'::text NOT NULL,
    unit_label_en text DEFAULT 'seat'::text NOT NULL,
    target_units integer NOT NULL,
    beneficiary_group text NOT NULL,
    group_label_ar text NOT NULL,
    group_label_en text,
    allow_partial boolean DEFAULT true NOT NULL,
    zakat_eligible boolean DEFAULT false NOT NULL,
    close_on_target boolean DEFAULT false NOT NULL,
    starts_at timestamp with time zone DEFAULT now() NOT NULL,
    ends_at timestamp with time zone NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT sponsorship_offer_check CHECK ((ends_at > starts_at)),
    CONSTRAINT sponsorship_offer_seat_price_check CHECK ((seat_price > (0)::numeric)),
    CONSTRAINT sponsorship_offer_target_units_check CHECK ((target_units > 0))
);


--
-- Name: sponsorship_pool; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.sponsorship_pool (
    sponsorship_offer_id uuid NOT NULL,
    balance numeric(12,2) DEFAULT 0 NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT sponsorship_pool_balance_check CHECK ((balance >= (0)::numeric))
);


--
-- Name: staff_user; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.staff_user (
    id uuid DEFAULT public.uuidv7() NOT NULL,
    email store.citext NOT NULL,
    name text NOT NULL,
    pass_hash text NOT NULL,
    totp_secret_enc bytea,
    roles text[] DEFAULT '{viewer}'::text[] NOT NULL,
    failed_count integer DEFAULT 0 NOT NULL,
    locked_until timestamp with time zone,
    is_active boolean DEFAULT true NOT NULL,
    last_login_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT staff_user_roles_check CHECK ((roles <@ ARRAY['admin'::text, 'store_manager'::text, 'finance'::text, 'allocation_officer'::text, 'fulfilment'::text, 'viewer'::text]))
);


--
-- Name: stock_movement; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.stock_movement (
    id bigint NOT NULL,
    book_format_id uuid NOT NULL,
    delta integer NOT NULL,
    reason text NOT NULL,
    ref_type text,
    ref_id text,
    note text,
    at timestamp with time zone DEFAULT now() NOT NULL,
    actor text NOT NULL,
    CONSTRAINT stock_movement_delta_check CHECK ((delta <> 0)),
    CONSTRAINT stock_movement_reason_check CHECK ((reason = ANY (ARRAY['restock'::text, 'sale'::text, 'return'::text, 'sponsor_distribution'::text, 'adjustment'::text])))
);


--
-- Name: stock_movement_id_seq; Type: SEQUENCE; Schema: store; Owner: -
--

ALTER TABLE store.stock_movement ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME store.stock_movement_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: unit_funding; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.unit_funding (
    sponsored_unit_id uuid NOT NULL,
    pool_contribution_id uuid NOT NULL,
    amount numeric(12,2) NOT NULL,
    CONSTRAINT unit_funding_amount_check CHECK ((amount > (0)::numeric))
);


--
-- Name: v_allocation_backlog; Type: VIEW; Schema: store; Owner: -
--

CREATE VIEW store.v_allocation_backlog AS
 SELECT u.product_id,
    p.slug,
    count(*) FILTER (WHERE (u.status = 'awaiting_allocation'::text)) AS awaiting,
    count(*) FILTER (WHERE ((u.status = 'awaiting_allocation'::text) AND (u.funded_at < (now() - '30 days'::interval)))) AS overdue_30,
    count(*) FILTER (WHERE (u.status = 'allocated'::text)) AS allocated,
    count(*) FILTER (WHERE (u.status = 'completed'::text)) AS completed,
    min(u.funded_at) FILTER (WHERE (u.status = 'awaiting_allocation'::text)) AS oldest_awaiting
   FROM (store.sponsored_unit u
     JOIN store.product p ON ((p.id = u.product_id)))
  GROUP BY u.product_id, p.slug;


--
-- Name: v_goal; Type: VIEW; Schema: store; Owner: -
--

CREATE VIEW store.v_goal AS
 SELECT p.id AS product_id,
    p.slug,
    p.type,
    COALESCE(c.target_amount, NULL::numeric) AS target_amount,
    so.target_units,
    (g.raised_amount + g.seed_raised) AS raised_amount,
    (g.funded_units + g.seed_units) AS funded_units,
    (g.donor_count + g.seed_donors) AS donor_count,
    COALESCE(c.ends_at, so.ends_at) AS ends_at
   FROM (((store.product p
     LEFT JOIN store.goal_progress g ON ((g.product_id = p.id)))
     LEFT JOIN store.cause_detail c ON ((c.product_id = p.id)))
     LEFT JOIN store.sponsorship_offer so ON (((so.product_id = p.id) AND so.is_active)));


--
-- Name: v_sales_daily; Type: VIEW; Schema: store; Owner: -
--

CREATE VIEW store.v_sales_daily AS
 SELECT ((o.paid_at AT TIME ZONE 'Asia/Riyadh'::text))::date AS day,
    l.kind,
    l.product_type,
    count(DISTINCT o.id) AS orders,
    sum(l.amount) AS gross,
    sum(l.vat_amount) AS vat,
    sum(l.refunded_amount) AS refunded
   FROM (store.orders o
     JOIN store.order_line l ON ((l.order_id = o.id)))
  WHERE (o.paid_at IS NOT NULL)
  GROUP BY (((o.paid_at AT TIME ZONE 'Asia/Riyadh'::text))::date), l.kind, l.product_type;


--
-- Name: zakat_ledger; Type: TABLE; Schema: store; Owner: -
--

CREATE TABLE store.zakat_ledger (
    id bigint NOT NULL,
    product_id uuid NOT NULL,
    direction text NOT NULL,
    amount numeric(12,2) NOT NULL,
    order_line_id uuid,
    reference text,
    at timestamp with time zone DEFAULT now() NOT NULL,
    actor text NOT NULL,
    CONSTRAINT zakat_ledger_amount_check CHECK ((amount > (0)::numeric)),
    CONSTRAINT zakat_ledger_direction_check CHECK ((direction = ANY (ARRAY['in'::text, 'out'::text, 'refund'::text])))
);


--
-- Name: v_zakat_balance; Type: VIEW; Schema: store; Owner: -
--

CREATE VIEW store.v_zakat_balance AS
 SELECT product_id,
    sum(amount) FILTER (WHERE (direction = 'in'::text)) AS received,
    sum(amount) FILTER (WHERE (direction = 'out'::text)) AS disbursed,
    sum(amount) FILTER (WHERE (direction = 'refund'::text)) AS refunded,
    (COALESCE(sum(amount) FILTER (WHERE (direction = 'in'::text)), (0)::numeric) - COALESCE(sum(amount) FILTER (WHERE (direction = ANY (ARRAY['out'::text, 'refund'::text]))), (0)::numeric)) AS balance
   FROM store.zakat_ledger
  GROUP BY product_id;


--
-- Name: zakat_ledger_id_seq; Type: SEQUENCE; Schema: store; Owner: -
--

ALTER TABLE store.zakat_ledger ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME store.zakat_ledger_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: allocation_batch allocation_batch_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.allocation_batch
    ADD CONSTRAINT allocation_batch_pkey PRIMARY KEY (id);


--
-- Name: audit_log audit_log_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.audit_log
    ADD CONSTRAINT audit_log_pkey PRIMARY KEY (id);


--
-- Name: beneficiary beneficiary_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.beneficiary
    ADD CONSTRAINT beneficiary_pkey PRIMARY KEY (id);


--
-- Name: book_detail book_detail_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.book_detail
    ADD CONSTRAINT book_detail_pkey PRIMARY KEY (product_id);


--
-- Name: book_format book_format_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.book_format
    ADD CONSTRAINT book_format_pkey PRIMARY KEY (id);


--
-- Name: cart_line cart_line_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.cart_line
    ADD CONSTRAINT cart_line_pkey PRIMARY KEY (id);


--
-- Name: cart cart_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.cart
    ADD CONSTRAINT cart_pkey PRIMARY KEY (id);


--
-- Name: category category_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.category
    ADD CONSTRAINT category_pkey PRIMARY KEY (id);


--
-- Name: cause_detail cause_detail_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.cause_detail
    ADD CONSTRAINT cause_detail_pkey PRIMARY KEY (product_id);


--
-- Name: cause_update cause_update_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.cause_update
    ADD CONSTRAINT cause_update_pkey PRIMARY KEY (id);


--
-- Name: course_detail course_detail_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.course_detail
    ADD CONSTRAINT course_detail_pkey PRIMARY KEY (product_id);


--
-- Name: course_module course_module_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.course_module
    ADD CONSTRAINT course_module_pkey PRIMARY KEY (id);


--
-- Name: customer_address customer_address_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.customer_address
    ADD CONSTRAINT customer_address_pkey PRIMARY KEY (id);


--
-- Name: customer customer_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.customer
    ADD CONSTRAINT customer_pkey PRIMARY KEY (id);


--
-- Name: dedication dedication_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.dedication
    ADD CONSTRAINT dedication_pkey PRIMARY KEY (id);


--
-- Name: distribution distribution_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.distribution
    ADD CONSTRAINT distribution_pkey PRIMARY KEY (id);


--
-- Name: document_counter document_counter_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.document_counter
    ADD CONSTRAINT document_counter_pkey PRIMARY KEY (series, year);


--
-- Name: donation_receipt donation_receipt_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.donation_receipt
    ADD CONSTRAINT donation_receipt_pkey PRIMARY KEY (id);


--
-- Name: download_grant download_grant_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.download_grant
    ADD CONSTRAINT download_grant_pkey PRIMARY KEY (id);


--
-- Name: download_log download_log_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.download_log
    ADD CONSTRAINT download_log_pkey PRIMARY KEY (id);


--
-- Name: enrolment enrolment_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.enrolment
    ADD CONSTRAINT enrolment_pkey PRIMARY KEY (id);


--
-- Name: goal_adjustment goal_adjustment_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.goal_adjustment
    ADD CONSTRAINT goal_adjustment_pkey PRIMARY KEY (id);


--
-- Name: goal_progress goal_progress_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.goal_progress
    ADD CONSTRAINT goal_progress_pkey PRIMARY KEY (product_id);


--
-- Name: idempotency_key idempotency_key_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.idempotency_key
    ADD CONSTRAINT idempotency_key_pkey PRIMARY KEY (scope, key);


--
-- Name: invoice invoice_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.invoice
    ADD CONSTRAINT invoice_pkey PRIMARY KEY (id);


--
-- Name: media_asset media_asset_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.media_asset
    ADD CONSTRAINT media_asset_pkey PRIMARY KEY (id);


--
-- Name: message_template message_template_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.message_template
    ADD CONSTRAINT message_template_pkey PRIMARY KEY (key, channel, locale);


--
-- Name: notification notification_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.notification
    ADD CONSTRAINT notification_pkey PRIMARY KEY (id);


--
-- Name: order_line order_line_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.order_line
    ADD CONSTRAINT order_line_pkey PRIMARY KEY (id);


--
-- Name: orders orders_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.orders
    ADD CONSTRAINT orders_pkey PRIMARY KEY (id);


--
-- Name: otp_challenge otp_challenge_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.otp_challenge
    ADD CONSTRAINT otp_challenge_pkey PRIMARY KEY (id);


--
-- Name: outbox_message outbox_message_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.outbox_message
    ADD CONSTRAINT outbox_message_pkey PRIMARY KEY (id);


--
-- Name: payment_event payment_event_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.payment_event
    ADD CONSTRAINT payment_event_pkey PRIMARY KEY (id);


--
-- Name: payment payment_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.payment
    ADD CONSTRAINT payment_pkey PRIMARY KEY (id);


--
-- Name: pool_contribution pool_contribution_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.pool_contribution
    ADD CONSTRAINT pool_contribution_pkey PRIMARY KEY (id);


--
-- Name: product product_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.product
    ADD CONSTRAINT product_pkey PRIMARY KEY (id);


--
-- Name: refresh_token refresh_token_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.refresh_token
    ADD CONSTRAINT refresh_token_pkey PRIMARY KEY (id);


--
-- Name: refund_line refund_line_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.refund_line
    ADD CONSTRAINT refund_line_pkey PRIMARY KEY (refund_id, order_line_id);


--
-- Name: refund refund_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.refund
    ADD CONSTRAINT refund_pkey PRIMARY KEY (id);


--
-- Name: setting setting_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.setting
    ADD CONSTRAINT setting_pkey PRIMARY KEY (key);


--
-- Name: shipment shipment_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.shipment
    ADD CONSTRAINT shipment_pkey PRIMARY KEY (id);


--
-- Name: sponsored_unit_event sponsored_unit_event_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.sponsored_unit_event
    ADD CONSTRAINT sponsored_unit_event_pkey PRIMARY KEY (id);


--
-- Name: sponsored_unit sponsored_unit_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.sponsored_unit
    ADD CONSTRAINT sponsored_unit_pkey PRIMARY KEY (id);


--
-- Name: sponsorship_offer sponsorship_offer_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.sponsorship_offer
    ADD CONSTRAINT sponsorship_offer_pkey PRIMARY KEY (id);


--
-- Name: sponsorship_pool sponsorship_pool_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.sponsorship_pool
    ADD CONSTRAINT sponsorship_pool_pkey PRIMARY KEY (sponsorship_offer_id);


--
-- Name: staff_user staff_user_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.staff_user
    ADD CONSTRAINT staff_user_pkey PRIMARY KEY (id);


--
-- Name: stock_movement stock_movement_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.stock_movement
    ADD CONSTRAINT stock_movement_pkey PRIMARY KEY (id);


--
-- Name: unit_funding unit_funding_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.unit_funding
    ADD CONSTRAINT unit_funding_pkey PRIMARY KEY (sponsored_unit_id, pool_contribution_id);


--
-- Name: zakat_ledger zakat_ledger_pkey; Type: CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.zakat_ledger
    ADD CONSTRAINT zakat_ledger_pkey PRIMARY KEY (id);


--
-- Name: beneficiary_code_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX beneficiary_code_key ON store.beneficiary USING btree (code);


--
-- Name: book_format_product_id_kind_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX book_format_product_id_kind_key ON store.book_format USING btree (product_id, kind);


--
-- Name: book_format_sku_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX book_format_sku_key ON store.book_format USING btree (sku);


--
-- Name: category_slug_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX category_slug_key ON store.category USING btree (slug);


--
-- Name: course_module_product_id_sort_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX course_module_product_id_sort_key ON store.course_module USING btree (product_id, sort);


--
-- Name: customer_mobile_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX customer_mobile_key ON store.customer USING btree (mobile);


--
-- Name: dedication_card_token_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX dedication_card_token_key ON store.dedication USING btree (card_token);


--
-- Name: dedication_order_line_id_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX dedication_order_line_id_key ON store.dedication USING btree (order_line_id);


--
-- Name: distribution_sponsored_unit_id_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX distribution_sponsored_unit_id_key ON store.distribution USING btree (sponsored_unit_id);


--
-- Name: donation_receipt_number_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX donation_receipt_number_key ON store.donation_receipt USING btree (number);


--
-- Name: download_grant_order_line_id_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX download_grant_order_line_id_key ON store.download_grant USING btree (order_line_id);


--
-- Name: enrolment_certificate_no_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX enrolment_certificate_no_key ON store.enrolment USING btree (certificate_no);


--
-- Name: invoice_number_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX invoice_number_key ON store.invoice USING btree (number);


--
-- Name: ix_audit_at; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_audit_at ON store.audit_log USING btree (at DESC);


--
-- Name: ix_audit_entity; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_audit_entity ON store.audit_log USING btree (entity, entity_id);


--
-- Name: ix_beneficiary_pick; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_beneficiary_pick ON store.beneficiary USING btree (beneficiary_group, eligibility_status, zakat_eligible);


--
-- Name: ix_cart_line_cart; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_cart_line_cart ON store.cart_line USING btree (cart_id);


--
-- Name: ix_cause_update_product; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_cause_update_product ON store.cause_update USING btree (product_id, occurred_on DESC);


--
-- Name: ix_customer_email; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_customer_email ON store.customer USING btree (email);


--
-- Name: ix_goal_adjustment_product; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_goal_adjustment_product ON store.goal_adjustment USING btree (product_id);


--
-- Name: ix_idempotency_created; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_idempotency_created ON store.idempotency_key USING btree (created_at);


--
-- Name: ix_invoice_order; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_invoice_order ON store.invoice USING btree (order_id);


--
-- Name: ix_notification_order; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_notification_order ON store.notification USING btree (related_order_id);


--
-- Name: ix_order_customer; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_order_customer ON store.orders USING btree (customer_id, placed_at DESC);


--
-- Name: ix_order_due; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_order_due ON store.orders USING btree (payment_due_at) WHERE (status = ANY (ARRAY['pending_payment'::text, 'payment_failed'::text]));


--
-- Name: ix_order_line_product; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_order_line_product ON store.order_line USING btree (product_id, kind);


--
-- Name: ix_order_status; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_order_status ON store.orders USING btree (status, placed_at);


--
-- Name: ix_otp_mobile_recent; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_otp_mobile_recent ON store.otp_challenge USING btree (mobile, created_at DESC);


--
-- Name: ix_outbox_pending; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_outbox_pending ON store.outbox_message USING btree (next_attempt_at) WHERE (processed_at IS NULL);


--
-- Name: ix_payment_order; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_payment_order ON store.payment USING btree (order_id);


--
-- Name: ix_payment_reconcile; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_payment_reconcile ON store.payment USING btree (status, created_at) WHERE (status = 'initiated'::text);


--
-- Name: ix_pool_fifo; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_pool_fifo ON store.pool_contribution USING btree (sponsorship_offer_id, created_at) WHERE (remaining > (0)::numeric);


--
-- Name: ix_product_listing; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_product_listing ON store.product USING btree (type, status, sort);


--
-- Name: ix_product_search; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_product_search ON store.product USING gin (search_vector);


--
-- Name: ix_product_trgm; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_product_trgm ON store.product USING gin (store.ar_normalize(title_ar) store.gin_trgm_ops);


--
-- Name: ix_receipt_order; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_receipt_order ON store.donation_receipt USING btree (order_id);


--
-- Name: ix_refresh_subject; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_refresh_subject ON store.refresh_token USING btree (subject_type, subject_id);


--
-- Name: ix_stock_movement_format; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_stock_movement_format ON store.stock_movement USING btree (book_format_id, at DESC);


--
-- Name: ix_unit_beneficiary; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_unit_beneficiary ON store.sponsored_unit USING btree (beneficiary_id);


--
-- Name: ix_unit_event_unit; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_unit_event_unit ON store.sponsored_unit_event USING btree (unit_id, at);


--
-- Name: ix_unit_fifo; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_unit_fifo ON store.sponsored_unit USING btree (product_id, funded_at) WHERE (status = 'awaiting_allocation'::text);


--
-- Name: ix_unit_orderline; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_unit_orderline ON store.sponsored_unit USING btree (order_line_id);


--
-- Name: ix_zakat_product; Type: INDEX; Schema: store; Owner: -
--

CREATE INDEX ix_zakat_product ON store.zakat_ledger USING btree (product_id, at);


--
-- Name: media_asset_storage_key_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX media_asset_storage_key_key ON store.media_asset USING btree (storage_key);


--
-- Name: order_line_order_id_line_no_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX order_line_order_id_line_no_key ON store.order_line USING btree (order_id, line_no);


--
-- Name: orders_reference_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX orders_reference_key ON store.orders USING btree (reference);


--
-- Name: payment_event_provider_provider_event_id_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX payment_event_provider_provider_event_id_key ON store.payment_event USING btree (provider, provider_event_id);


--
-- Name: payment_provider_provider_payment_id_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX payment_provider_provider_payment_id_key ON store.payment USING btree (provider, provider_payment_id);


--
-- Name: pool_contribution_order_line_id_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX pool_contribution_order_line_id_key ON store.pool_contribution USING btree (order_line_id);


--
-- Name: product_slug_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX product_slug_key ON store.product USING btree (slug);


--
-- Name: refresh_token_token_hash_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX refresh_token_token_hash_key ON store.refresh_token USING btree (token_hash);


--
-- Name: shipment_order_id_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX shipment_order_id_key ON store.shipment USING btree (order_id);


--
-- Name: staff_user_email_key; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX staff_user_email_key ON store.staff_user USING btree (email);


--
-- Name: ux_address_default; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX ux_address_default ON store.customer_address USING btree (customer_id) WHERE is_default;


--
-- Name: ux_beneficiary_mobile; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX ux_beneficiary_mobile ON store.beneficiary USING btree (mobile_hash) WHERE (mobile_hash IS NOT NULL);


--
-- Name: ux_cart_customer_active; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX ux_cart_customer_active ON store.cart USING btree (customer_id) WHERE ((status = 'active'::text) AND (customer_id IS NOT NULL));


--
-- Name: ux_enrolment_live; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX ux_enrolment_live ON store.enrolment USING btree (customer_id, product_id) WHERE (status = ANY (ARRAY['pending_sync'::text, 'active'::text, 'completed'::text]));


--
-- Name: ux_notification_once; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX ux_notification_once ON store.notification USING btree (outbox_message_id, channel, recipient) WHERE (outbox_message_id IS NOT NULL);


--
-- Name: ux_offer_one_active; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX ux_offer_one_active ON store.sponsorship_offer USING btree (product_id) WHERE is_active;


--
-- Name: ux_one_general_fund; Type: INDEX; Schema: store; Owner: -
--

CREATE UNIQUE INDEX ux_one_general_fund ON store.cause_detail USING btree ((true)) WHERE is_general_fund;


--
-- Name: allocation_batch allocation_batch_product_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.allocation_batch
    ADD CONSTRAINT allocation_batch_product_id_fkey FOREIGN KEY (product_id) REFERENCES store.product(id);


--
-- Name: beneficiary beneficiary_customer_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.beneficiary
    ADD CONSTRAINT beneficiary_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES store.customer(id);


--
-- Name: book_detail book_detail_product_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.book_detail
    ADD CONSTRAINT book_detail_product_id_fkey FOREIGN KEY (product_id) REFERENCES store.product(id) ON DELETE CASCADE;


--
-- Name: book_format book_format_file_asset_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.book_format
    ADD CONSTRAINT book_format_file_asset_id_fkey FOREIGN KEY (file_asset_id) REFERENCES store.media_asset(id);


--
-- Name: book_format book_format_product_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.book_format
    ADD CONSTRAINT book_format_product_id_fkey FOREIGN KEY (product_id) REFERENCES store.product(id) ON DELETE CASCADE;


--
-- Name: cart cart_customer_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.cart
    ADD CONSTRAINT cart_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES store.customer(id) ON DELETE CASCADE;


--
-- Name: cart_line cart_line_book_format_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.cart_line
    ADD CONSTRAINT cart_line_book_format_id_fkey FOREIGN KEY (book_format_id) REFERENCES store.book_format(id);


--
-- Name: cart_line cart_line_cart_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.cart_line
    ADD CONSTRAINT cart_line_cart_id_fkey FOREIGN KEY (cart_id) REFERENCES store.cart(id) ON DELETE CASCADE;


--
-- Name: cart_line cart_line_product_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.cart_line
    ADD CONSTRAINT cart_line_product_id_fkey FOREIGN KEY (product_id) REFERENCES store.product(id);


--
-- Name: cause_detail cause_detail_product_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.cause_detail
    ADD CONSTRAINT cause_detail_product_id_fkey FOREIGN KEY (product_id) REFERENCES store.product(id) ON DELETE CASCADE;


--
-- Name: cause_update cause_update_asset_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.cause_update
    ADD CONSTRAINT cause_update_asset_id_fkey FOREIGN KEY (asset_id) REFERENCES store.media_asset(id);


--
-- Name: cause_update cause_update_product_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.cause_update
    ADD CONSTRAINT cause_update_product_id_fkey FOREIGN KEY (product_id) REFERENCES store.product(id) ON DELETE CASCADE;


--
-- Name: course_detail course_detail_product_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.course_detail
    ADD CONSTRAINT course_detail_product_id_fkey FOREIGN KEY (product_id) REFERENCES store.product(id) ON DELETE CASCADE;


--
-- Name: course_module course_module_product_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.course_module
    ADD CONSTRAINT course_module_product_id_fkey FOREIGN KEY (product_id) REFERENCES store.product(id) ON DELETE CASCADE;


--
-- Name: customer_address customer_address_customer_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.customer_address
    ADD CONSTRAINT customer_address_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES store.customer(id) ON DELETE CASCADE;


--
-- Name: dedication dedication_order_line_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.dedication
    ADD CONSTRAINT dedication_order_line_id_fkey FOREIGN KEY (order_line_id) REFERENCES store.order_line(id) ON DELETE CASCADE;


--
-- Name: distribution distribution_book_format_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.distribution
    ADD CONSTRAINT distribution_book_format_id_fkey FOREIGN KEY (book_format_id) REFERENCES store.book_format(id);


--
-- Name: distribution distribution_proof_asset_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.distribution
    ADD CONSTRAINT distribution_proof_asset_id_fkey FOREIGN KEY (proof_asset_id) REFERENCES store.media_asset(id);


--
-- Name: distribution distribution_sponsored_unit_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.distribution
    ADD CONSTRAINT distribution_sponsored_unit_id_fkey FOREIGN KEY (sponsored_unit_id) REFERENCES store.sponsored_unit(id);


--
-- Name: donation_receipt donation_receipt_order_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.donation_receipt
    ADD CONSTRAINT donation_receipt_order_id_fkey FOREIGN KEY (order_id) REFERENCES store.orders(id);


--
-- Name: donation_receipt donation_receipt_pdf_asset_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.donation_receipt
    ADD CONSTRAINT donation_receipt_pdf_asset_id_fkey FOREIGN KEY (pdf_asset_id) REFERENCES store.media_asset(id);


--
-- Name: donation_receipt donation_receipt_references_receipt_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.donation_receipt
    ADD CONSTRAINT donation_receipt_references_receipt_id_fkey FOREIGN KEY (references_receipt_id) REFERENCES store.donation_receipt(id);


--
-- Name: download_grant download_grant_book_format_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.download_grant
    ADD CONSTRAINT download_grant_book_format_id_fkey FOREIGN KEY (book_format_id) REFERENCES store.book_format(id);


--
-- Name: download_grant download_grant_customer_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.download_grant
    ADD CONSTRAINT download_grant_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES store.customer(id);


--
-- Name: download_grant download_grant_order_line_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.download_grant
    ADD CONSTRAINT download_grant_order_line_id_fkey FOREIGN KEY (order_line_id) REFERENCES store.order_line(id);


--
-- Name: download_log download_log_grant_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.download_log
    ADD CONSTRAINT download_log_grant_id_fkey FOREIGN KEY (grant_id) REFERENCES store.download_grant(id);


--
-- Name: enrolment enrolment_customer_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.enrolment
    ADD CONSTRAINT enrolment_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES store.customer(id);


--
-- Name: enrolment enrolment_order_line_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.enrolment
    ADD CONSTRAINT enrolment_order_line_id_fkey FOREIGN KEY (order_line_id) REFERENCES store.order_line(id);


--
-- Name: enrolment enrolment_product_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.enrolment
    ADD CONSTRAINT enrolment_product_id_fkey FOREIGN KEY (product_id) REFERENCES store.product(id);


--
-- Name: enrolment fk_enrolment_unit; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.enrolment
    ADD CONSTRAINT fk_enrolment_unit FOREIGN KEY (sponsored_unit_id) REFERENCES store.sponsored_unit(id);


--
-- Name: goal_adjustment goal_adjustment_product_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.goal_adjustment
    ADD CONSTRAINT goal_adjustment_product_id_fkey FOREIGN KEY (product_id) REFERENCES store.product(id);


--
-- Name: goal_adjustment goal_adjustment_sponsorship_offer_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.goal_adjustment
    ADD CONSTRAINT goal_adjustment_sponsorship_offer_id_fkey FOREIGN KEY (sponsorship_offer_id) REFERENCES store.sponsorship_offer(id);


--
-- Name: goal_progress goal_progress_product_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.goal_progress
    ADD CONSTRAINT goal_progress_product_id_fkey FOREIGN KEY (product_id) REFERENCES store.product(id) ON DELETE CASCADE;


--
-- Name: invoice invoice_order_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.invoice
    ADD CONSTRAINT invoice_order_id_fkey FOREIGN KEY (order_id) REFERENCES store.orders(id);


--
-- Name: invoice invoice_pdf_asset_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.invoice
    ADD CONSTRAINT invoice_pdf_asset_id_fkey FOREIGN KEY (pdf_asset_id) REFERENCES store.media_asset(id);


--
-- Name: invoice invoice_references_invoice_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.invoice
    ADD CONSTRAINT invoice_references_invoice_id_fkey FOREIGN KEY (references_invoice_id) REFERENCES store.invoice(id);


--
-- Name: notification notification_outbox_message_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.notification
    ADD CONSTRAINT notification_outbox_message_id_fkey FOREIGN KEY (outbox_message_id) REFERENCES store.outbox_message(id);


--
-- Name: notification notification_related_order_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.notification
    ADD CONSTRAINT notification_related_order_id_fkey FOREIGN KEY (related_order_id) REFERENCES store.orders(id);


--
-- Name: order_line order_line_book_format_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.order_line
    ADD CONSTRAINT order_line_book_format_id_fkey FOREIGN KEY (book_format_id) REFERENCES store.book_format(id);


--
-- Name: order_line order_line_order_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.order_line
    ADD CONSTRAINT order_line_order_id_fkey FOREIGN KEY (order_id) REFERENCES store.orders(id) ON DELETE CASCADE;


--
-- Name: order_line order_line_product_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.order_line
    ADD CONSTRAINT order_line_product_id_fkey FOREIGN KEY (product_id) REFERENCES store.product(id);


--
-- Name: order_line order_line_sponsorship_offer_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.order_line
    ADD CONSTRAINT order_line_sponsorship_offer_id_fkey FOREIGN KEY (sponsorship_offer_id) REFERENCES store.sponsorship_offer(id);


--
-- Name: orders orders_customer_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.orders
    ADD CONSTRAINT orders_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES store.customer(id);


--
-- Name: payment payment_order_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.payment
    ADD CONSTRAINT payment_order_id_fkey FOREIGN KEY (order_id) REFERENCES store.orders(id);


--
-- Name: pool_contribution pool_contribution_order_line_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.pool_contribution
    ADD CONSTRAINT pool_contribution_order_line_id_fkey FOREIGN KEY (order_line_id) REFERENCES store.order_line(id);


--
-- Name: pool_contribution pool_contribution_sponsorship_offer_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.pool_contribution
    ADD CONSTRAINT pool_contribution_sponsorship_offer_id_fkey FOREIGN KEY (sponsorship_offer_id) REFERENCES store.sponsorship_offer(id);


--
-- Name: product product_category_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.product
    ADD CONSTRAINT product_category_id_fkey FOREIGN KEY (category_id) REFERENCES store.category(id) ON DELETE SET NULL;


--
-- Name: product product_cover_asset_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.product
    ADD CONSTRAINT product_cover_asset_id_fkey FOREIGN KEY (cover_asset_id) REFERENCES store.media_asset(id) ON DELETE SET NULL;


--
-- Name: refund_line refund_line_order_line_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.refund_line
    ADD CONSTRAINT refund_line_order_line_id_fkey FOREIGN KEY (order_line_id) REFERENCES store.order_line(id);


--
-- Name: refund_line refund_line_refund_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.refund_line
    ADD CONSTRAINT refund_line_refund_id_fkey FOREIGN KEY (refund_id) REFERENCES store.refund(id) ON DELETE CASCADE;


--
-- Name: refund refund_order_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.refund
    ADD CONSTRAINT refund_order_id_fkey FOREIGN KEY (order_id) REFERENCES store.orders(id);


--
-- Name: refund refund_payment_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.refund
    ADD CONSTRAINT refund_payment_id_fkey FOREIGN KEY (payment_id) REFERENCES store.payment(id);


--
-- Name: shipment shipment_order_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.shipment
    ADD CONSTRAINT shipment_order_id_fkey FOREIGN KEY (order_id) REFERENCES store.orders(id);


--
-- Name: sponsored_unit sponsored_unit_allocation_batch_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.sponsored_unit
    ADD CONSTRAINT sponsored_unit_allocation_batch_id_fkey FOREIGN KEY (allocation_batch_id) REFERENCES store.allocation_batch(id);


--
-- Name: sponsored_unit sponsored_unit_beneficiary_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.sponsored_unit
    ADD CONSTRAINT sponsored_unit_beneficiary_id_fkey FOREIGN KEY (beneficiary_id) REFERENCES store.beneficiary(id);


--
-- Name: sponsored_unit_event sponsored_unit_event_unit_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.sponsored_unit_event
    ADD CONSTRAINT sponsored_unit_event_unit_id_fkey FOREIGN KEY (unit_id) REFERENCES store.sponsored_unit(id) ON DELETE CASCADE;


--
-- Name: sponsored_unit sponsored_unit_order_line_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.sponsored_unit
    ADD CONSTRAINT sponsored_unit_order_line_id_fkey FOREIGN KEY (order_line_id) REFERENCES store.order_line(id);


--
-- Name: sponsored_unit sponsored_unit_product_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.sponsored_unit
    ADD CONSTRAINT sponsored_unit_product_id_fkey FOREIGN KEY (product_id) REFERENCES store.product(id);


--
-- Name: sponsored_unit sponsored_unit_sponsorship_offer_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.sponsored_unit
    ADD CONSTRAINT sponsored_unit_sponsorship_offer_id_fkey FOREIGN KEY (sponsorship_offer_id) REFERENCES store.sponsorship_offer(id);


--
-- Name: sponsorship_offer sponsorship_offer_product_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.sponsorship_offer
    ADD CONSTRAINT sponsorship_offer_product_id_fkey FOREIGN KEY (product_id) REFERENCES store.product(id) ON DELETE CASCADE;


--
-- Name: sponsorship_pool sponsorship_pool_sponsorship_offer_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.sponsorship_pool
    ADD CONSTRAINT sponsorship_pool_sponsorship_offer_id_fkey FOREIGN KEY (sponsorship_offer_id) REFERENCES store.sponsorship_offer(id);


--
-- Name: stock_movement stock_movement_book_format_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.stock_movement
    ADD CONSTRAINT stock_movement_book_format_id_fkey FOREIGN KEY (book_format_id) REFERENCES store.book_format(id);


--
-- Name: unit_funding unit_funding_pool_contribution_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.unit_funding
    ADD CONSTRAINT unit_funding_pool_contribution_id_fkey FOREIGN KEY (pool_contribution_id) REFERENCES store.pool_contribution(id);


--
-- Name: unit_funding unit_funding_sponsored_unit_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.unit_funding
    ADD CONSTRAINT unit_funding_sponsored_unit_id_fkey FOREIGN KEY (sponsored_unit_id) REFERENCES store.sponsored_unit(id);


--
-- Name: zakat_ledger zakat_ledger_order_line_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.zakat_ledger
    ADD CONSTRAINT zakat_ledger_order_line_id_fkey FOREIGN KEY (order_line_id) REFERENCES store.order_line(id);


--
-- Name: zakat_ledger zakat_ledger_product_id_fkey; Type: FK CONSTRAINT; Schema: store; Owner: -
--

ALTER TABLE ONLY store.zakat_ledger
    ADD CONSTRAINT zakat_ledger_product_id_fkey FOREIGN KEY (product_id) REFERENCES store.product(id);


--
-- PostgreSQL database dump complete
--


