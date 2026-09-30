using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace Ta3lom.Store.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {

        // Copied verbatim from db/reference/001_store_schema.sql (SchemaParityTests compares the result).
        private const string ArNormalizeFunction = @"CREATE OR REPLACE FUNCTION store.ar_normalize(t text) RETURNS text
LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
  SELECT lower(translate(
           regexp_replace(coalesce(t,''), '[\u064B-\u0652\u0640]', '', 'g'),
           'أإآٱةى', 'اااايهي'))
$$;";

        private const string ExpressionIndexes = @"
CREATE INDEX ix_product_trgm ON store.product USING gin (store.ar_normalize(title_ar) store.gin_trgm_ops);
CREATE UNIQUE INDEX ux_one_general_fund ON store.cause_detail ((true)) WHERE is_general_fund;";

        private const string ReportingViews = @"SET LOCAL search_path = store, public;
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
FROM zakat_ledger GROUP BY product_id;";

        // database/01 §7. Roles are cluster-wide and created by operations (S-03); a missing role is skipped.
        // store_app: DML on every table, INSERT/SELECT only on the append-only ledgers; store_report: views only.
        internal const string Grants = @"
DO $grants$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'store_app') THEN
        GRANT USAGE ON SCHEMA store TO store_app;
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA store TO store_app;
        REVOKE UPDATE, DELETE ON store.audit_log, store.payment_event, store.zakat_ledger,
                                 store.stock_movement, store.sponsored_unit_event FROM store_app;
        GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA store TO store_app;
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'store_owner') THEN
            ALTER DEFAULT PRIVILEGES FOR ROLE store_owner IN SCHEMA store
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO store_app;
            ALTER DEFAULT PRIVILEGES FOR ROLE store_owner IN SCHEMA store
                GRANT USAGE, SELECT ON SEQUENCES TO store_app;
        END IF;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'store_report') THEN
        GRANT USAGE ON SCHEMA store TO store_report;
        GRANT SELECT ON store.v_goal, store.v_sales_daily, store.v_allocation_backlog, store.v_zakat_balance TO store_report;
    END IF;
END
$grants$;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "store");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:store.citext", ",,")
                .Annotation("Npgsql:PostgresExtension:store.pg_trgm", ",,");

            // Arabic normalisation (FR-S03); product.search_vector is generated from it, so it comes first.
            migrationBuilder.Sql(ArNormalizeFunction);

            migrationBuilder.CreateSequence(
                name: "beneficiary_code_seq",
                schema: "store");

            migrationBuilder.CreateSequence(
                name: "order_ref_seq",
                schema: "store",
                startValue: 1000L);

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    entity = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: true),
                    diff = table.Column<string>(type: "jsonb", nullable: true),
                    ip_hash = table.Column<string>(type: "character(64)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("audit_log_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "category",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name_ar = table.Column<string>(type: "text", nullable: false),
                    name_en = table.Column<string>(type: "text", nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "true")
                },
                constraints: table =>
                {
                    table.PrimaryKey("category_pkey", x => x.id);
                    table.CheckConstraint("category_slug_check", "(slug ~ '^[a-z0-9-]{2,60}$'::text)");
                });

            migrationBuilder.CreateTable(
                name: "customer",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    mobile = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "citext", nullable: true),
                    locale = table.Column<string>(type: "character(2)", nullable: false, defaultValueSql: "'ar'::bpchar"),
                    anonymous_default = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    marketing_opt_in = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'active'::text"),
                    pdpl_consent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("customer_pkey", x => x.id);
                    table.CheckConstraint("customer_locale_check", "(locale = ANY (ARRAY['ar'::bpchar, 'en'::bpchar]))");
                    table.CheckConstraint("customer_mobile_check", "(mobile ~ '^\\+9665[0-9]{8}$'::text)");
                    table.CheckConstraint("customer_status_check", "(status = ANY (ARRAY['active'::text, 'blocked'::text, 'anonymised'::text]))");
                });

            migrationBuilder.CreateTable(
                name: "document_counter",
                schema: "store",
                columns: table => new
                {
                    series = table.Column<string>(type: "text", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("document_counter_pkey", x => new { x.series, x.year });
                    table.CheckConstraint("document_counter_series_check", "(series = ANY (ARRAY['INV'::text, 'CN'::text, 'DR'::text, 'DRC'::text]))");
                });

            migrationBuilder.CreateTable(
                name: "idempotency_key",
                schema: "store",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: false),
                    request_hash = table.Column<string>(type: "character(64)", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    response = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("idempotency_key_pkey", x => new { x.scope, x.key });
                });

            migrationBuilder.CreateTable(
                name: "media_asset",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    kind = table.Column<string>(type: "text", nullable: false),
                    storage_key = table.Column<string>(type: "text", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "true"),
                    mime = table.Column<string>(type: "text", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    alt_ar = table.Column<string>(type: "text", nullable: true),
                    alt_en = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("media_asset_pkey", x => x.id);
                    table.CheckConstraint("ck_media_alt", "((kind <> 'image'::text) OR (alt_ar IS NOT NULL))");
                    table.CheckConstraint("ck_media_private_book", "((kind <> 'book_file'::text) OR (is_public = false))");
                    table.CheckConstraint("media_asset_kind_check", "(kind = ANY (ARRAY['image'::text, 'pdf'::text, 'book_file'::text]))");
                    table.CheckConstraint("media_asset_size_bytes_check", "(size_bytes > 0)");
                });

            migrationBuilder.CreateTable(
                name: "message_template",
                schema: "store",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: false),
                    locale = table.Column<string>(type: "character(2)", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("message_template_pkey", x => new { x.key, x.channel, x.locale });
                    table.CheckConstraint("message_template_channel_check", "(channel = ANY (ARRAY['sms'::text, 'email'::text]))");
                    table.CheckConstraint("message_template_locale_check", "(locale = ANY (ARRAY['ar'::bpchar, 'en'::bpchar]))");
                });

            migrationBuilder.CreateTable(
                name: "otp_challenge",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    mobile = table.Column<string>(type: "text", nullable: false),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    code_hash = table.Column<string>(type: "character(64)", nullable: false),
                    attempts = table.Column<short>(type: "smallint", nullable: false, defaultValueSql: "0"),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ip_hash = table.Column<string>(type: "character(64)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("otp_challenge_pkey", x => x.id);
                    table.CheckConstraint("otp_challenge_purpose_check", "(purpose = ANY (ARRAY['login'::text, 'beneficiary_invite'::text]))");
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("outbox_message_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_event",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    provider = table.Column<string>(type: "text", nullable: false),
                    provider_event_id = table.Column<string>(type: "text", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    signature_valid = table.Column<bool>(type: "boolean", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("payment_event_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_token",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    subject_type = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_agent = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("refresh_token_pkey", x => x.id);
                    table.CheckConstraint("refresh_token_subject_type_check", "(subject_type = ANY (ARRAY['customer'::text, 'staff'::text]))");
                });

            migrationBuilder.CreateTable(
                name: "setting",
                schema: "store",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("setting_pkey", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "staff_user",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    email = table.Column<string>(type: "citext", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    pass_hash = table.Column<string>(type: "text", nullable: false),
                    totp_secret_enc = table.Column<byte[]>(type: "bytea", nullable: true),
                    roles = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{viewer}'::text[]"),
                    failed_count = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "true"),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("staff_user_pkey", x => x.id);
                    table.CheckConstraint("staff_user_roles_check", "(roles <@ ARRAY['admin'::text, 'store_manager'::text, 'finance'::text, 'allocation_officer'::text, 'fulfilment'::text, 'viewer'::text])");
                });

            migrationBuilder.CreateTable(
                name: "beneficiary",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    code = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    beneficiary_group = table.Column<string>(type: "text", nullable: false),
                    region = table.Column<string>(type: "text", nullable: false),
                    full_name_enc = table.Column<byte[]>(type: "bytea", nullable: false),
                    mobile_enc = table.Column<byte[]>(type: "bytea", nullable: true),
                    mobile_hash = table.Column<string>(type: "character(64)", nullable: true),
                    guardian_name_enc = table.Column<byte[]>(type: "bytea", nullable: true),
                    organisation_name = table.Column<string>(type: "text", nullable: true),
                    eligibility_status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'pending'::text"),
                    zakat_eligible = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    eligibility_notes = table.Column<string>(type: "text", nullable: true),
                    verified_by = table.Column<string>(type: "text", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("beneficiary_pkey", x => x.id);
                    table.CheckConstraint("beneficiary_eligibility_status_check", "(eligibility_status = ANY (ARRAY['pending'::text, 'approved'::text, 'rejected'::text, 'inactive'::text]))");
                    table.CheckConstraint("beneficiary_kind_check", "(kind = ANY (ARRAY['student'::text, 'teacher'::text, 'parent'::text, 'school'::text, 'library'::text]))");
                    table.ForeignKey(
                        name: "beneficiary_customer_id_fkey",
                        column: x => x.customer_id,
                        principalSchema: "store",
                        principalTable: "customer",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "cart",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'active'::text"),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("cart_pkey", x => x.id);
                    table.CheckConstraint("cart_status_check", "(status = ANY (ARRAY['active'::text, 'converted'::text, 'merged'::text, 'expired'::text]))");
                    table.ForeignKey(
                        name: "cart_customer_id_fkey",
                        column: x => x.customer_id,
                        principalSchema: "store",
                        principalTable: "customer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_address",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_name = table.Column<string>(type: "text", nullable: false),
                    recipient_mobile = table.Column<string>(type: "text", nullable: false),
                    city = table.Column<string>(type: "text", nullable: false),
                    district = table.Column<string>(type: "text", nullable: true),
                    short_address = table.Column<string>(type: "text", nullable: true),
                    street = table.Column<string>(type: "text", nullable: true),
                    building_no = table.Column<string>(type: "text", nullable: true),
                    postal_code = table.Column<string>(type: "text", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("customer_address_pkey", x => x.id);
                    table.CheckConstraint("customer_address_postal_code_check", "(postal_code ~ '^[0-9]{5}$'::text)");
                    table.CheckConstraint("customer_address_recipient_mobile_check", "(recipient_mobile ~ '^\\+9665[0-9]{8}$'::text)");
                    table.CheckConstraint("customer_address_short_address_check", "(short_address ~ '^[A-Z]{4}[0-9]{4}$'::text)");
                    table.ForeignKey(
                        name: "customer_address_customer_id_fkey",
                        column: x => x.customer_id,
                        principalSchema: "store",
                        principalTable: "customer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    reference = table.Column<string>(type: "text", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'pending_payment'::text"),
                    currency = table.Column<string>(type: "character(3)", nullable: false, defaultValueSql: "'SAR'::bpchar"),
                    purchases_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValueSql: "0"),
                    shipping_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValueSql: "0"),
                    gifts_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValueSql: "0"),
                    vat_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValueSql: "0"),
                    grand_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    refunded_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValueSql: "0"),
                    is_anonymous = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    locale = table.Column<string>(type: "character(2)", nullable: false, defaultValueSql: "'ar'::bpchar"),
                    contact_name = table.Column<string>(type: "text", nullable: false),
                    contact_mobile = table.Column<string>(type: "text", nullable: false),
                    contact_email = table.Column<string>(type: "citext", nullable: false),
                    shipping_address = table.Column<string>(type: "jsonb", nullable: true),
                    source_cart_id = table.Column<Guid>(type: "uuid", nullable: true),
                    terms_accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    placed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    payment_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fulfilled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("orders_pkey", x => x.id);
                    table.CheckConstraint("ck_order_total", "(grand_total = ((purchases_total + shipping_total) + gifts_total))");
                    table.CheckConstraint("orders_currency_check", "(currency = 'SAR'::bpchar)");
                    table.CheckConstraint("orders_grand_total_check", "(grand_total > (0)::numeric)");
                    table.CheckConstraint("orders_status_check", "(status = ANY (ARRAY['pending_payment'::text, 'paid'::text, 'payment_failed'::text, 'payment_review'::text, 'expired'::text, 'cancelled'::text, 'fulfilled'::text, 'partially_refunded'::text, 'refunded'::text]))");
                    table.ForeignKey(
                        name: "orders_customer_id_fkey",
                        column: x => x.customer_id,
                        principalSchema: "store",
                        principalTable: "customer",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "product",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    type = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'draft'::text"),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cover_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title_ar = table.Column<string>(type: "text", nullable: false),
                    title_en = table.Column<string>(type: "text", nullable: true),
                    summary_ar = table.Column<string>(type: "text", nullable: false),
                    summary_en = table.Column<string>(type: "text", nullable: true),
                    body_ar = table.Column<string>(type: "text", nullable: true),
                    body_en = table.Column<string>(type: "text", nullable: true),
                    badge = table.Column<string>(type: "text", nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    seo_title_ar = table.Column<string>(type: "text", nullable: true),
                    seo_title_en = table.Column<string>(type: "text", nullable: true),
                    seo_desc_ar = table.Column<string>(type: "text", nullable: true),
                    seo_desc_en = table.Column<string>(type: "text", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple'::regconfig, ar_normalize(((((((COALESCE(title_ar, ''::text) || ' '::text) || COALESCE(title_en, ''::text)) || ' '::text) || COALESCE(summary_ar, ''::text)) || ' '::text) || COALESCE(summary_en, ''::text))))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("product_pkey", x => x.id);
                    table.CheckConstraint("product_badge_check", "(badge = ANY (ARRAY['best'::text, 'new'::text, 'urgent'::text]))");
                    table.CheckConstraint("product_slug_check", "(slug ~ '^[a-z0-9-]{2,80}$'::text)");
                    table.CheckConstraint("product_status_check", "(status = ANY (ARRAY['draft'::text, 'published'::text, 'archived'::text]))");
                    table.CheckConstraint("product_title_ar_check", "((length(title_ar) >= 2) AND (length(title_ar) <= 160))");
                    table.CheckConstraint("product_type_check", "(type = ANY (ARRAY['course'::text, 'book'::text, 'cause'::text]))");
                    table.ForeignKey(
                        name: "product_category_id_fkey",
                        column: x => x.category_id,
                        principalSchema: "store",
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "product_cover_asset_id_fkey",
                        column: x => x.cover_asset_id,
                        principalSchema: "store",
                        principalTable: "media_asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "donation_receipt",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'receipt'::text"),
                    number = table.Column<string>(type: "text", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    donor_name = table.Column<string>(type: "text", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    zakat_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValueSql: "0"),
                    references_receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pdf_asset_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("donation_receipt_pkey", x => x.id);
                    table.CheckConstraint("donation_receipt_kind_check", "(kind = ANY (ARRAY['receipt'::text, 'cancellation'::text]))");
                    table.ForeignKey(
                        name: "donation_receipt_order_id_fkey",
                        column: x => x.order_id,
                        principalSchema: "store",
                        principalTable: "orders",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "donation_receipt_pdf_asset_id_fkey",
                        column: x => x.pdf_asset_id,
                        principalSchema: "store",
                        principalTable: "media_asset",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "donation_receipt_references_receipt_id_fkey",
                        column: x => x.references_receipt_id,
                        principalSchema: "store",
                        principalTable: "donation_receipt",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "invoice",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    is_tax_invoice = table.Column<bool>(type: "boolean", nullable: false),
                    seller_vat_no = table.Column<string>(type: "text", nullable: true),
                    net_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    vat_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    gross_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    references_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pdf_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    zatca_uuid = table.Column<Guid>(type: "uuid", nullable: true),
                    zatca_icv = table.Column<long>(type: "bigint", nullable: true),
                    zatca_pih = table.Column<string>(type: "text", nullable: true),
                    zatca_hash = table.Column<string>(type: "text", nullable: true),
                    zatca_qr = table.Column<string>(type: "text", nullable: true),
                    zatca_status = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("invoice_pkey", x => x.id);
                    table.CheckConstraint("invoice_check", "(gross_total = (net_total + vat_total))");
                    table.CheckConstraint("invoice_kind_check", "(kind = ANY (ARRAY['invoice'::text, 'credit_note'::text]))");
                    table.CheckConstraint("invoice_zatca_status_check", "(zatca_status = ANY (ARRAY['not_required'::text, 'pending'::text, 'reported'::text, 'cleared'::text, 'rejected'::text]))");
                    table.ForeignKey(
                        name: "invoice_order_id_fkey",
                        column: x => x.order_id,
                        principalSchema: "store",
                        principalTable: "orders",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "invoice_pdf_asset_id_fkey",
                        column: x => x.pdf_asset_id,
                        principalSchema: "store",
                        principalTable: "media_asset",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "invoice_references_invoice_id_fkey",
                        column: x => x.references_invoice_id,
                        principalSchema: "store",
                        principalTable: "invoice",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "notification",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    channel = table.Column<string>(type: "text", nullable: false),
                    recipient = table.Column<string>(type: "text", nullable: false),
                    template_key = table.Column<string>(type: "text", nullable: false),
                    locale = table.Column<string>(type: "character(2)", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'queued'::text"),
                    provider_msg_id = table.Column<string>(type: "text", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    related_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outbox_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("notification_pkey", x => x.id);
                    table.CheckConstraint("notification_channel_check", "(channel = ANY (ARRAY['sms'::text, 'email'::text]))");
                    table.CheckConstraint("notification_status_check", "(status = ANY (ARRAY['queued'::text, 'sent'::text, 'failed'::text]))");
                    table.ForeignKey(
                        name: "notification_outbox_message_id_fkey",
                        column: x => x.outbox_message_id,
                        principalSchema: "store",
                        principalTable: "outbox_message",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "notification_related_order_id_fkey",
                        column: x => x.related_order_id,
                        principalSchema: "store",
                        principalTable: "orders",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "payment",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    provider_payment_id = table.Column<string>(type: "text", nullable: true),
                    method = table.Column<string>(type: "text", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'initiated'::text"),
                    failure_code = table.Column<string>(type: "text", nullable: true),
                    failure_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("payment_pkey", x => x.id);
                    table.CheckConstraint("payment_amount_check", "(amount > (0)::numeric)");
                    table.CheckConstraint("payment_method_check", "(method = ANY (ARRAY['mada'::text, 'applepay'::text, 'card'::text, 'stcpay'::text, 'bnpl'::text]))");
                    table.CheckConstraint("payment_status_check", "(status = ANY (ARRAY['initiated'::text, 'authorized'::text, 'captured'::text, 'failed'::text, 'voided'::text, 'refunded'::text, 'partially_refunded'::text]))");
                    table.ForeignKey(
                        name: "payment_order_id_fkey",
                        column: x => x.order_id,
                        principalSchema: "store",
                        principalTable: "orders",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "shipment",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'preparing'::text"),
                    carrier = table.Column<string>(type: "text", nullable: true),
                    tracking_no = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "jsonb", nullable: false),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("shipment_pkey", x => x.id);
                    table.CheckConstraint("shipment_status_check", "(status = ANY (ARRAY['preparing'::text, 'shipped'::text, 'delivered'::text, 'returned'::text]))");
                    table.ForeignKey(
                        name: "shipment_order_id_fkey",
                        column: x => x.order_id,
                        principalSchema: "store",
                        principalTable: "orders",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "allocation_batch",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("allocation_batch_pkey", x => x.id);
                    table.ForeignKey(
                        name: "allocation_batch_product_id_fkey",
                        column: x => x.product_id,
                        principalSchema: "store",
                        principalTable: "product",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "book_detail",
                schema: "store",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_ar = table.Column<string>(type: "text", nullable: false),
                    author_en = table.Column<string>(type: "text", nullable: true),
                    pages = table.Column<int>(type: "integer", nullable: true),
                    isbn = table.Column<string>(type: "text", nullable: true),
                    published_year = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("book_detail_pkey", x => x.product_id);
                    table.CheckConstraint("book_detail_pages_check", "(pages > 0)");
                    table.ForeignKey(
                        name: "book_detail_product_id_fkey",
                        column: x => x.product_id,
                        principalSchema: "store",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "book_format",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    sku = table.Column<string>(type: "text", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    compare_at_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "true"),
                    stock_on_hand = table.Column<int>(type: "integer", nullable: true),
                    stock_reserved = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    weight_grams = table.Column<int>(type: "integer", nullable: true),
                    low_stock_threshold = table.Column<int>(type: "integer", nullable: true),
                    file_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("book_format_pkey", x => x.id);
                    table.CheckConstraint("book_format_check", "((compare_at_price IS NULL) OR (compare_at_price > price))");
                    table.CheckConstraint("book_format_kind_check", "(kind = ANY (ARRAY['print'::text, 'digital'::text]))");
                    table.CheckConstraint("book_format_low_stock_threshold_check", "(low_stock_threshold >= 0)");
                    table.CheckConstraint("book_format_price_check", "(price >= (0)::numeric)");
                    table.CheckConstraint("book_format_stock_on_hand_check", "(stock_on_hand >= 0)");
                    table.CheckConstraint("book_format_stock_reserved_check", "(stock_reserved >= 0)");
                    table.CheckConstraint("ck_format_digital", "((kind <> 'digital'::text) OR (file_asset_id IS NOT NULL))");
                    table.CheckConstraint("ck_format_print", "((kind <> 'print'::text) OR (stock_on_hand IS NOT NULL))");
                    table.CheckConstraint("ck_format_reserve", "(stock_reserved <= COALESCE(stock_on_hand, 0))");
                    table.ForeignKey(
                        name: "book_format_file_asset_id_fkey",
                        column: x => x.file_asset_id,
                        principalSchema: "store",
                        principalTable: "media_asset",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "book_format_product_id_fkey",
                        column: x => x.product_id,
                        principalSchema: "store",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cause_detail",
                schema: "store",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    unit_label_ar = table.Column<string>(type: "text", nullable: true),
                    unit_label_en = table.Column<string>(type: "text", nullable: true),
                    target_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 12, scale: 2, nullable: false),
                    allow_custom_amount = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "true"),
                    min_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValueSql: "10"),
                    zakat_eligible = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    beneficiaries_ar = table.Column<string>(type: "text", nullable: true),
                    beneficiaries_en = table.Column<string>(type: "text", nullable: true),
                    close_on_target = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    is_general_fund = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("cause_detail_pkey", x => x.product_id);
                    table.CheckConstraint("cause_detail_min_amount_check", "(min_amount > (0)::numeric)");
                    table.CheckConstraint("cause_detail_target_amount_check", "(target_amount > (0)::numeric)");
                    table.CheckConstraint("cause_detail_unit_amount_check", "((unit_amount IS NULL) OR (unit_amount > (0)::numeric))");
                    table.CheckConstraint("ck_cause_label", "((unit_amount IS NULL) OR (unit_label_ar IS NOT NULL))");
                    table.CheckConstraint("ck_cause_mode", "((unit_amount IS NOT NULL) OR allow_custom_amount)");
                    table.ForeignKey(
                        name: "cause_detail_product_id_fkey",
                        column: x => x.product_id,
                        principalSchema: "store",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cause_update",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title_ar = table.Column<string>(type: "text", nullable: false),
                    title_en = table.Column<string>(type: "text", nullable: true),
                    body_ar = table.Column<string>(type: "text", nullable: true),
                    body_en = table.Column<string>(type: "text", nullable: true),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    units_delivered = table.Column<int>(type: "integer", nullable: true),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "true"),
                    notify_donors = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("cause_update_pkey", x => x.id);
                    table.CheckConstraint("cause_update_units_delivered_check", "(units_delivered >= 0)");
                    table.ForeignKey(
                        name: "cause_update_asset_id_fkey",
                        column: x => x.asset_id,
                        principalSchema: "store",
                        principalTable: "media_asset",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "cause_update_product_id_fkey",
                        column: x => x.product_id,
                        principalSchema: "store",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "course_detail",
                schema: "store",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    compare_at_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    lessons = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    level_ar = table.Column<string>(type: "text", nullable: true),
                    level_en = table.Column<string>(type: "text", nullable: true),
                    instructor_ar = table.Column<string>(type: "text", nullable: false),
                    instructor_en = table.Column<string>(type: "text", nullable: true),
                    access_days = table.Column<int>(type: "integer", nullable: true),
                    lms_course_ref = table.Column<string>(type: "text", nullable: true),
                    learner_count = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("course_detail_pkey", x => x.product_id);
                    table.CheckConstraint("course_detail_access_days_check", "((access_days IS NULL) OR (access_days > 0))");
                    table.CheckConstraint("course_detail_check", "((compare_at_price IS NULL) OR (compare_at_price > price))");
                    table.CheckConstraint("course_detail_duration_minutes_check", "(duration_minutes >= 0)");
                    table.CheckConstraint("course_detail_lessons_check", "(lessons >= 0)");
                    table.CheckConstraint("course_detail_price_check", "(price >= (0)::numeric)");
                    table.ForeignKey(
                        name: "course_detail_product_id_fkey",
                        column: x => x.product_id,
                        principalSchema: "store",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "course_module",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    title_ar = table.Column<string>(type: "text", nullable: false),
                    title_en = table.Column<string>(type: "text", nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("course_module_pkey", x => x.id);
                    table.ForeignKey(
                        name: "course_module_product_id_fkey",
                        column: x => x.product_id,
                        principalSchema: "store",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "goal_progress",
                schema: "store",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raised_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 12, scale: 2, nullable: false, defaultValueSql: "0"),
                    funded_units = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    donor_count = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    zakat_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 12, scale: 2, nullable: false, defaultValueSql: "0"),
                    seed_raised = table.Column<decimal>(type: "numeric(14,2)", precision: 12, scale: 2, nullable: false, defaultValueSql: "0"),
                    seed_units = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    seed_donors = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    last_gift_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("goal_progress_pkey", x => x.product_id);
                    table.ForeignKey(
                        name: "goal_progress_product_id_fkey",
                        column: x => x.product_id,
                        principalSchema: "store",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sponsorship_offer",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    unit_label_ar = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'مقعد'::text"),
                    unit_label_en = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'seat'::text"),
                    target_units = table.Column<int>(type: "integer", nullable: false),
                    beneficiary_group = table.Column<string>(type: "text", nullable: false),
                    group_label_ar = table.Column<string>(type: "text", nullable: false),
                    group_label_en = table.Column<string>(type: "text", nullable: true),
                    allow_partial = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "true"),
                    zakat_eligible = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    close_on_target = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "true"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("sponsorship_offer_pkey", x => x.id);
                    table.CheckConstraint("sponsorship_offer_check", "(ends_at > starts_at)");
                    table.CheckConstraint("sponsorship_offer_seat_price_check", "(seat_price > (0)::numeric)");
                    table.CheckConstraint("sponsorship_offer_target_units_check", "(target_units > 0)");
                    table.ForeignKey(
                        name: "sponsorship_offer_product_id_fkey",
                        column: x => x.product_id,
                        principalSchema: "store",
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refund",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'requested'::text"),
                    provider_refund_id = table.Column<string>(type: "text", nullable: true),
                    requested_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("refund_pkey", x => x.id);
                    table.CheckConstraint("refund_amount_check", "(amount > (0)::numeric)");
                    table.CheckConstraint("refund_status_check", "(status = ANY (ARRAY['requested'::text, 'succeeded'::text, 'failed'::text]))");
                    table.ForeignKey(
                        name: "refund_order_id_fkey",
                        column: x => x.order_id,
                        principalSchema: "store",
                        principalTable: "orders",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "refund_payment_id_fkey",
                        column: x => x.payment_id,
                        principalSchema: "store",
                        principalTable: "payment",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "cart_line",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    cart_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    book_format_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "1"),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    intent = table.Column<string>(type: "text", nullable: true),
                    dedication = table.Column<string>(type: "jsonb", nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("cart_line_pkey", x => x.id);
                    table.CheckConstraint("cart_line_amount_check", "(amount > (0)::numeric)");
                    table.CheckConstraint("cart_line_intent_check", "(intent = ANY (ARRAY['sadaqa'::text, 'zakat'::text]))");
                    table.CheckConstraint("cart_line_kind_check", "(kind = ANY (ARRAY['buy'::text, 'sponsor'::text, 'donate'::text]))");
                    table.CheckConstraint("cart_line_mode_check", "(mode = ANY (ARRAY['units'::text, 'amount'::text]))");
                    table.CheckConstraint("cart_line_quantity_check", "((quantity >= 1) AND (quantity <= 999))");
                    table.CheckConstraint("cart_line_unit_price_check", "(unit_price >= (0)::numeric)");
                    table.CheckConstraint("ck_cart_amount_qty", "((mode <> 'amount'::text) OR (quantity = 1))");
                    table.CheckConstraint("ck_cart_buy_nointent", "((kind <> 'buy'::text) OR ((intent IS NULL) AND (dedication IS NULL)))");
                    table.CheckConstraint("ck_cart_buy_units", "((kind <> 'buy'::text) OR (mode = 'units'::text))");
                    table.ForeignKey(
                        name: "cart_line_book_format_id_fkey",
                        column: x => x.book_format_id,
                        principalSchema: "store",
                        principalTable: "book_format",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "cart_line_cart_id_fkey",
                        column: x => x.cart_id,
                        principalSchema: "store",
                        principalTable: "cart",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "cart_line_product_id_fkey",
                        column: x => x.product_id,
                        principalSchema: "store",
                        principalTable: "product",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "stock_movement",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    book_format_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delta = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    ref_type = table.Column<string>(type: "text", nullable: true),
                    ref_id = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("stock_movement_pkey", x => x.id);
                    table.CheckConstraint("stock_movement_delta_check", "(delta <> 0)");
                    table.CheckConstraint("stock_movement_reason_check", "(reason = ANY (ARRAY['restock'::text, 'sale'::text, 'return'::text, 'sponsor_distribution'::text, 'adjustment'::text]))");
                    table.ForeignKey(
                        name: "stock_movement_book_format_id_fkey",
                        column: x => x.book_format_id,
                        principalSchema: "store",
                        principalTable: "book_format",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "goal_adjustment",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    units = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    sponsorship_offer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pair_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("goal_adjustment_pkey", x => x.id);
                    table.CheckConstraint("goal_adjustment_check", "((amount <> (0)::numeric) OR (units <> 0))");
                    table.CheckConstraint("goal_adjustment_kind_check", "(kind = ANY (ARRAY['pool_sweep_out'::text, 'pool_sweep_in'::text, 'correction'::text]))");
                    table.ForeignKey(
                        name: "goal_adjustment_product_id_fkey",
                        column: x => x.product_id,
                        principalSchema: "store",
                        principalTable: "product",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "goal_adjustment_sponsorship_offer_id_fkey",
                        column: x => x.sponsorship_offer_id,
                        principalSchema: "store",
                        principalTable: "sponsorship_offer",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "order_line",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<short>(type: "smallint", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_type = table.Column<string>(type: "text", nullable: false),
                    book_format_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sponsorship_offer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 12, scale: 2, nullable: false, defaultValueSql: "0"),
                    vat_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValueSql: "0"),
                    intent = table.Column<string>(type: "text", nullable: true),
                    title_ar = table.Column<string>(type: "text", nullable: false),
                    title_en = table.Column<string>(type: "text", nullable: true),
                    fulfilment_status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'pending'::text"),
                    refunded_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValueSql: "0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("order_line_pkey", x => x.id);
                    table.CheckConstraint("ck_line_buy_vat", "((kind = 'buy'::text) OR (vat_amount = (0)::numeric))");
                    table.CheckConstraint("ck_line_intent", "((kind = 'donate'::text) OR (kind = 'sponsor'::text) OR (intent IS NULL))");
                    table.CheckConstraint("ck_line_offer", "((kind <> 'sponsor'::text) OR (sponsorship_offer_id IS NOT NULL))");
                    table.CheckConstraint("ck_line_refund", "(refunded_amount <= amount)");
                    table.CheckConstraint("order_line_amount_check", "(amount > (0)::numeric)");
                    table.CheckConstraint("order_line_fulfilment_status_check", "(fulfilment_status = ANY (ARRAY['pending'::text, 'enrolled'::text, 'pending_sync'::text, 'available'::text, 'preparing'::text, 'shipped'::text, 'delivered'::text, 'returned'::text, 'funded'::text, 'received'::text, 'refunded'::text]))");
                    table.CheckConstraint("order_line_intent_check", "(intent = ANY (ARRAY['sadaqa'::text, 'zakat'::text]))");
                    table.CheckConstraint("order_line_kind_check", "(kind = ANY (ARRAY['buy'::text, 'sponsor'::text, 'donate'::text]))");
                    table.CheckConstraint("order_line_mode_check", "(mode = ANY (ARRAY['units'::text, 'amount'::text]))");
                    table.CheckConstraint("order_line_product_type_check", "(product_type = ANY (ARRAY['course'::text, 'book'::text, 'cause'::text]))");
                    table.CheckConstraint("order_line_quantity_check", "((quantity >= 1) AND (quantity <= 999))");
                    table.ForeignKey(
                        name: "order_line_book_format_id_fkey",
                        column: x => x.book_format_id,
                        principalSchema: "store",
                        principalTable: "book_format",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "order_line_order_id_fkey",
                        column: x => x.order_id,
                        principalSchema: "store",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "order_line_product_id_fkey",
                        column: x => x.product_id,
                        principalSchema: "store",
                        principalTable: "product",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "order_line_sponsorship_offer_id_fkey",
                        column: x => x.sponsorship_offer_id,
                        principalSchema: "store",
                        principalTable: "sponsorship_offer",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "sponsorship_pool",
                schema: "store",
                columns: table => new
                {
                    sponsorship_offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValueSql: "0"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("sponsorship_pool_pkey", x => x.sponsorship_offer_id);
                    table.CheckConstraint("sponsorship_pool_balance_check", "(balance >= (0)::numeric)");
                    table.ForeignKey(
                        name: "sponsorship_pool_sponsorship_offer_id_fkey",
                        column: x => x.sponsorship_offer_id,
                        principalSchema: "store",
                        principalTable: "sponsorship_offer",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "dedication",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_name = table.Column<string>(type: "text", nullable: false),
                    to_mobile = table.Column<string>(type: "text", nullable: false),
                    from_name = table.Column<string>(type: "text", nullable: true),
                    template_key = table.Column<string>(type: "text", nullable: false),
                    custom_text = table.Column<string>(type: "text", nullable: true),
                    card_token = table.Column<string>(type: "text", nullable: false),
                    sms_status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'pending'::text"),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("dedication_pkey", x => x.id);
                    table.CheckConstraint("dedication_custom_text_check", "(length(custom_text) <= 140)");
                    table.CheckConstraint("dedication_sms_status_check", "(sms_status = ANY (ARRAY['pending'::text, 'sent'::text, 'failed'::text, 'skipped'::text]))");
                    table.CheckConstraint("dedication_to_mobile_check", "(to_mobile ~ '^\\+9665[0-9]{8}$'::text)");
                    table.CheckConstraint("dedication_to_name_check", "((length(to_name) >= 2) AND (length(to_name) <= 60))");
                    table.ForeignKey(
                        name: "dedication_order_line_id_fkey",
                        column: x => x.order_line_id,
                        principalSchema: "store",
                        principalTable: "order_line",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "download_grant",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    book_format_id = table.Column<Guid>(type: "uuid", nullable: false),
                    max_downloads = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "5"),
                    download_count = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("download_grant_pkey", x => x.id);
                    table.CheckConstraint("download_grant_check", "(download_count <= max_downloads)");
                    table.ForeignKey(
                        name: "download_grant_book_format_id_fkey",
                        column: x => x.book_format_id,
                        principalSchema: "store",
                        principalTable: "book_format",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "download_grant_customer_id_fkey",
                        column: x => x.customer_id,
                        principalSchema: "store",
                        principalTable: "customer",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "download_grant_order_line_id_fkey",
                        column: x => x.order_line_id,
                        principalSchema: "store",
                        principalTable: "order_line",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "pool_contribution",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    sponsorship_offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    remaining = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    is_zakat = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    swept_to_fund_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pool_contribution_pkey", x => x.id);
                    table.CheckConstraint("pool_contribution_amount_check", "(amount > (0)::numeric)");
                    table.CheckConstraint("pool_contribution_check", "(remaining <= amount)");
                    table.CheckConstraint("pool_contribution_remaining_check", "(remaining >= (0)::numeric)");
                    table.ForeignKey(
                        name: "pool_contribution_order_line_id_fkey",
                        column: x => x.order_line_id,
                        principalSchema: "store",
                        principalTable: "order_line",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "pool_contribution_sponsorship_offer_id_fkey",
                        column: x => x.sponsorship_offer_id,
                        principalSchema: "store",
                        principalTable: "sponsorship_offer",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "refund_line",
                schema: "store",
                columns: table => new
                {
                    refund_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    units = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("refund_line_pkey", x => new { x.refund_id, x.order_line_id });
                    table.CheckConstraint("refund_line_amount_check", "(amount > (0)::numeric)");
                    table.ForeignKey(
                        name: "refund_line_order_line_id_fkey",
                        column: x => x.order_line_id,
                        principalSchema: "store",
                        principalTable: "order_line",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "refund_line_refund_id_fkey",
                        column: x => x.refund_id,
                        principalSchema: "store",
                        principalTable: "refund",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sponsored_unit",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sponsorship_offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    has_zakat = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'awaiting_allocation'::text"),
                    beneficiary_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allocation_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    funded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    allocated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    allocated_by = table.Column<string>(type: "text", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lapsed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("sponsored_unit_pkey", x => x.id);
                    table.CheckConstraint("ck_unit_alloc", "((status <> ALL (ARRAY['allocated'::text, 'completed'::text])) OR (beneficiary_id IS NOT NULL))");
                    table.CheckConstraint("ck_unit_source", "((source = 'full'::text) = (order_line_id IS NOT NULL))");
                    table.CheckConstraint("sponsored_unit_source_check", "(source = ANY (ARRAY['full'::text, 'pooled'::text]))");
                    table.CheckConstraint("sponsored_unit_status_check", "(status = ANY (ARRAY['awaiting_allocation'::text, 'allocated'::text, 'completed'::text, 'lapsed'::text, 'cancelled'::text]))");
                    table.ForeignKey(
                        name: "sponsored_unit_allocation_batch_id_fkey",
                        column: x => x.allocation_batch_id,
                        principalSchema: "store",
                        principalTable: "allocation_batch",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "sponsored_unit_beneficiary_id_fkey",
                        column: x => x.beneficiary_id,
                        principalSchema: "store",
                        principalTable: "beneficiary",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "sponsored_unit_order_line_id_fkey",
                        column: x => x.order_line_id,
                        principalSchema: "store",
                        principalTable: "order_line",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "sponsored_unit_product_id_fkey",
                        column: x => x.product_id,
                        principalSchema: "store",
                        principalTable: "product",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "sponsored_unit_sponsorship_offer_id_fkey",
                        column: x => x.sponsorship_offer_id,
                        principalSchema: "store",
                        principalTable: "sponsorship_offer",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "zakat_ledger",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference = table.Column<string>(type: "text", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("zakat_ledger_pkey", x => x.id);
                    table.CheckConstraint("zakat_ledger_amount_check", "(amount > (0)::numeric)");
                    table.CheckConstraint("zakat_ledger_direction_check", "(direction = ANY (ARRAY['in'::text, 'out'::text, 'refund'::text]))");
                    table.ForeignKey(
                        name: "zakat_ledger_order_line_id_fkey",
                        column: x => x.order_line_id,
                        principalSchema: "store",
                        principalTable: "order_line",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "zakat_ledger_product_id_fkey",
                        column: x => x.product_id,
                        principalSchema: "store",
                        principalTable: "product",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "download_log",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ip_hash = table.Column<string>(type: "character(64)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("download_log_pkey", x => x.id);
                    table.ForeignKey(
                        name: "download_log_grant_id_fkey",
                        column: x => x.grant_id,
                        principalSchema: "store",
                        principalTable: "download_grant",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "distribution",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    sponsored_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    book_format_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivered_on = table.Column<DateOnly>(type: "date", nullable: true),
                    proof_asset_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("distribution_pkey", x => x.id);
                    table.ForeignKey(
                        name: "distribution_book_format_id_fkey",
                        column: x => x.book_format_id,
                        principalSchema: "store",
                        principalTable: "book_format",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "distribution_proof_asset_id_fkey",
                        column: x => x.proof_asset_id,
                        principalSchema: "store",
                        principalTable: "media_asset",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "distribution_sponsored_unit_id_fkey",
                        column: x => x.sponsored_unit_id,
                        principalSchema: "store",
                        principalTable: "sponsored_unit",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "enrolment",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sponsored_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'active'::text"),
                    progress_pct = table.Column<short>(type: "smallint", nullable: false, defaultValueSql: "0"),
                    lms_enrolment_ref = table.Column<string>(type: "text", nullable: true),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    certificate_no = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("enrolment_pkey", x => x.id);
                    table.CheckConstraint("ck_enrol_source", "(((source = 'purchase'::text) AND (order_line_id IS NOT NULL)) OR ((source = 'sponsored'::text) AND (sponsored_unit_id IS NOT NULL)) OR (source = 'manual'::text))");
                    table.CheckConstraint("enrolment_progress_pct_check", "((progress_pct >= 0) AND (progress_pct <= 100))");
                    table.CheckConstraint("enrolment_source_check", "(source = ANY (ARRAY['purchase'::text, 'sponsored'::text, 'manual'::text]))");
                    table.CheckConstraint("enrolment_status_check", "(status = ANY (ARRAY['pending_sync'::text, 'active'::text, 'completed'::text, 'revoked'::text, 'expired'::text]))");
                    table.ForeignKey(
                        name: "enrolment_customer_id_fkey",
                        column: x => x.customer_id,
                        principalSchema: "store",
                        principalTable: "customer",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "enrolment_order_line_id_fkey",
                        column: x => x.order_line_id,
                        principalSchema: "store",
                        principalTable: "order_line",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "enrolment_product_id_fkey",
                        column: x => x.product_id,
                        principalSchema: "store",
                        principalTable: "product",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_enrolment_unit",
                        column: x => x.sponsored_unit_id,
                        principalSchema: "store",
                        principalTable: "sponsored_unit",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "sponsored_unit_event",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "text", nullable: true),
                    to_status = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("sponsored_unit_event_pkey", x => x.id);
                    table.ForeignKey(
                        name: "sponsored_unit_event_unit_id_fkey",
                        column: x => x.unit_id,
                        principalSchema: "store",
                        principalTable: "sponsored_unit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "unit_funding",
                schema: "store",
                columns: table => new
                {
                    sponsored_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pool_contribution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("unit_funding_pkey", x => new { x.sponsored_unit_id, x.pool_contribution_id });
                    table.CheckConstraint("unit_funding_amount_check", "(amount > (0)::numeric)");
                    table.ForeignKey(
                        name: "unit_funding_pool_contribution_id_fkey",
                        column: x => x.pool_contribution_id,
                        principalSchema: "store",
                        principalTable: "pool_contribution",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "unit_funding_sponsored_unit_id_fkey",
                        column: x => x.sponsored_unit_id,
                        principalSchema: "store",
                        principalTable: "sponsored_unit",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_at",
                schema: "store",
                table: "audit_log",
                column: "at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_audit_entity",
                schema: "store",
                table: "audit_log",
                columns: new[] { "entity", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "beneficiary_code_key",
                schema: "store",
                table: "beneficiary",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_beneficiary_pick",
                schema: "store",
                table: "beneficiary",
                columns: new[] { "beneficiary_group", "eligibility_status", "zakat_eligible" });

            migrationBuilder.CreateIndex(
                name: "ux_beneficiary_mobile",
                schema: "store",
                table: "beneficiary",
                column: "mobile_hash",
                unique: true,
                filter: "(mobile_hash IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "book_format_product_id_kind_key",
                schema: "store",
                table: "book_format",
                columns: new[] { "product_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "book_format_sku_key",
                schema: "store",
                table: "book_format",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_cart_customer_active",
                schema: "store",
                table: "cart",
                column: "customer_id",
                unique: true,
                filter: "((status = 'active'::text) AND (customer_id IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "ix_cart_line_cart",
                schema: "store",
                table: "cart_line",
                column: "cart_id");

            migrationBuilder.CreateIndex(
                name: "category_slug_key",
                schema: "store",
                table: "category",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cause_update_product",
                schema: "store",
                table: "cause_update",
                columns: new[] { "product_id", "occurred_on" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "course_module_product_id_sort_key",
                schema: "store",
                table: "course_module",
                columns: new[] { "product_id", "sort" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "customer_mobile_key",
                schema: "store",
                table: "customer",
                column: "mobile",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_email",
                schema: "store",
                table: "customer",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ux_address_default",
                schema: "store",
                table: "customer_address",
                column: "customer_id",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "dedication_card_token_key",
                schema: "store",
                table: "dedication",
                column: "card_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "dedication_order_line_id_key",
                schema: "store",
                table: "dedication",
                column: "order_line_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "distribution_sponsored_unit_id_key",
                schema: "store",
                table: "distribution",
                column: "sponsored_unit_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "donation_receipt_number_key",
                schema: "store",
                table: "donation_receipt",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_receipt_order",
                schema: "store",
                table: "donation_receipt",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "download_grant_order_line_id_key",
                schema: "store",
                table: "download_grant",
                column: "order_line_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "enrolment_certificate_no_key",
                schema: "store",
                table: "enrolment",
                column: "certificate_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_enrolment_live",
                schema: "store",
                table: "enrolment",
                columns: new[] { "customer_id", "product_id" },
                unique: true,
                filter: "(status = ANY (ARRAY['pending_sync'::text, 'active'::text, 'completed'::text]))");

            migrationBuilder.CreateIndex(
                name: "ix_goal_adjustment_product",
                schema: "store",
                table: "goal_adjustment",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_created",
                schema: "store",
                table: "idempotency_key",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "invoice_number_key",
                schema: "store",
                table: "invoice",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_order",
                schema: "store",
                table: "invoice",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "media_asset_storage_key_key",
                schema: "store",
                table: "media_asset",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_order",
                schema: "store",
                table: "notification",
                column: "related_order_id");

            migrationBuilder.CreateIndex(
                name: "ux_notification_once",
                schema: "store",
                table: "notification",
                columns: new[] { "outbox_message_id", "channel", "recipient" },
                unique: true,
                filter: "(outbox_message_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_order_line_product",
                schema: "store",
                table: "order_line",
                columns: new[] { "product_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "order_line_order_id_line_no_key",
                schema: "store",
                table: "order_line",
                columns: new[] { "order_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_customer",
                schema: "store",
                table: "orders",
                columns: new[] { "customer_id", "placed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_order_due",
                schema: "store",
                table: "orders",
                column: "payment_due_at",
                filter: "(status = ANY (ARRAY['pending_payment'::text, 'payment_failed'::text]))");

            migrationBuilder.CreateIndex(
                name: "ix_order_status",
                schema: "store",
                table: "orders",
                columns: new[] { "status", "placed_at" });

            migrationBuilder.CreateIndex(
                name: "orders_reference_key",
                schema: "store",
                table: "orders",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_otp_mobile_recent",
                schema: "store",
                table: "otp_challenge",
                columns: new[] { "mobile", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "store",
                table: "outbox_message",
                column: "next_attempt_at",
                filter: "(processed_at IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_payment_order",
                schema: "store",
                table: "payment",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_reconcile",
                schema: "store",
                table: "payment",
                columns: new[] { "status", "created_at" },
                filter: "(status = 'initiated'::text)");

            migrationBuilder.CreateIndex(
                name: "payment_provider_provider_payment_id_key",
                schema: "store",
                table: "payment",
                columns: new[] { "provider", "provider_payment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "payment_event_provider_provider_event_id_key",
                schema: "store",
                table: "payment_event",
                columns: new[] { "provider", "provider_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pool_fifo",
                schema: "store",
                table: "pool_contribution",
                columns: new[] { "sponsorship_offer_id", "created_at" },
                filter: "(remaining > (0)::numeric)");

            migrationBuilder.CreateIndex(
                name: "pool_contribution_order_line_id_key",
                schema: "store",
                table: "pool_contribution",
                column: "order_line_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_listing",
                schema: "store",
                table: "product",
                columns: new[] { "type", "status", "sort" });

            migrationBuilder.CreateIndex(
                name: "ix_product_search",
                schema: "store",
                table: "product",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "product_slug_key",
                schema: "store",
                table: "product",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_subject",
                schema: "store",
                table: "refresh_token",
                columns: new[] { "subject_type", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "refresh_token_token_hash_key",
                schema: "store",
                table: "refresh_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "shipment_order_id_key",
                schema: "store",
                table: "shipment",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unit_beneficiary",
                schema: "store",
                table: "sponsored_unit",
                column: "beneficiary_id");

            migrationBuilder.CreateIndex(
                name: "ix_unit_fifo",
                schema: "store",
                table: "sponsored_unit",
                columns: new[] { "product_id", "funded_at" },
                filter: "(status = 'awaiting_allocation'::text)");

            migrationBuilder.CreateIndex(
                name: "ix_unit_orderline",
                schema: "store",
                table: "sponsored_unit",
                column: "order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_unit_event_unit",
                schema: "store",
                table: "sponsored_unit_event",
                columns: new[] { "unit_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ux_offer_one_active",
                schema: "store",
                table: "sponsorship_offer",
                column: "product_id",
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "staff_user_email_key",
                schema: "store",
                table: "staff_user",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_format",
                schema: "store",
                table: "stock_movement",
                columns: new[] { "book_format_id", "at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_zakat_product",
                schema: "store",
                table: "zakat_ledger",
                columns: new[] { "product_id", "at" });

            // Indexes EF cannot express (expressions), the reporting views and the role grants.
            migrationBuilder.Sql(ExpressionIndexes);
            migrationBuilder.Sql(ReportingViews);
            migrationBuilder.Sql(Grants);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS store.v_zakat_balance, store.v_allocation_backlog, store.v_sales_daily, store.v_goal;");

            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "store");

            migrationBuilder.DropTable(
                name: "book_detail",
                schema: "store");

            migrationBuilder.DropTable(
                name: "cart_line",
                schema: "store");

            migrationBuilder.DropTable(
                name: "cause_detail",
                schema: "store");

            migrationBuilder.DropTable(
                name: "cause_update",
                schema: "store");

            migrationBuilder.DropTable(
                name: "course_detail",
                schema: "store");

            migrationBuilder.DropTable(
                name: "course_module",
                schema: "store");

            migrationBuilder.DropTable(
                name: "customer_address",
                schema: "store");

            migrationBuilder.DropTable(
                name: "dedication",
                schema: "store");

            migrationBuilder.DropTable(
                name: "distribution",
                schema: "store");

            migrationBuilder.DropTable(
                name: "document_counter",
                schema: "store");

            migrationBuilder.DropTable(
                name: "donation_receipt",
                schema: "store");

            migrationBuilder.DropTable(
                name: "download_log",
                schema: "store");

            migrationBuilder.DropTable(
                name: "enrolment",
                schema: "store");

            migrationBuilder.DropTable(
                name: "goal_adjustment",
                schema: "store");

            migrationBuilder.DropTable(
                name: "goal_progress",
                schema: "store");

            migrationBuilder.DropTable(
                name: "idempotency_key",
                schema: "store");

            migrationBuilder.DropTable(
                name: "invoice",
                schema: "store");

            migrationBuilder.DropTable(
                name: "message_template",
                schema: "store");

            migrationBuilder.DropTable(
                name: "notification",
                schema: "store");

            migrationBuilder.DropTable(
                name: "otp_challenge",
                schema: "store");

            migrationBuilder.DropTable(
                name: "payment_event",
                schema: "store");

            migrationBuilder.DropTable(
                name: "refresh_token",
                schema: "store");

            migrationBuilder.DropTable(
                name: "refund_line",
                schema: "store");

            migrationBuilder.DropTable(
                name: "setting",
                schema: "store");

            migrationBuilder.DropTable(
                name: "shipment",
                schema: "store");

            migrationBuilder.DropTable(
                name: "sponsored_unit_event",
                schema: "store");

            migrationBuilder.DropTable(
                name: "sponsorship_pool",
                schema: "store");

            migrationBuilder.DropTable(
                name: "staff_user",
                schema: "store");

            migrationBuilder.DropTable(
                name: "stock_movement",
                schema: "store");

            migrationBuilder.DropTable(
                name: "unit_funding",
                schema: "store");

            migrationBuilder.DropTable(
                name: "zakat_ledger",
                schema: "store");

            migrationBuilder.DropTable(
                name: "cart",
                schema: "store");

            migrationBuilder.DropTable(
                name: "download_grant",
                schema: "store");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "store");

            migrationBuilder.DropTable(
                name: "refund",
                schema: "store");

            migrationBuilder.DropTable(
                name: "pool_contribution",
                schema: "store");

            migrationBuilder.DropTable(
                name: "sponsored_unit",
                schema: "store");

            migrationBuilder.DropTable(
                name: "payment",
                schema: "store");

            migrationBuilder.DropTable(
                name: "allocation_batch",
                schema: "store");

            migrationBuilder.DropTable(
                name: "beneficiary",
                schema: "store");

            migrationBuilder.DropTable(
                name: "order_line",
                schema: "store");

            migrationBuilder.DropTable(
                name: "book_format",
                schema: "store");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "store");

            migrationBuilder.DropTable(
                name: "sponsorship_offer",
                schema: "store");

            migrationBuilder.DropTable(
                name: "customer",
                schema: "store");

            migrationBuilder.DropTable(
                name: "product",
                schema: "store");

            migrationBuilder.DropTable(
                name: "category",
                schema: "store");

            migrationBuilder.DropTable(
                name: "media_asset",
                schema: "store");

            migrationBuilder.DropSequence(
                name: "beneficiary_code_seq",
                schema: "store");

            migrationBuilder.DropSequence(
                name: "order_ref_seq",
                schema: "store");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS store.ar_normalize(text);");
        }
    }
}
