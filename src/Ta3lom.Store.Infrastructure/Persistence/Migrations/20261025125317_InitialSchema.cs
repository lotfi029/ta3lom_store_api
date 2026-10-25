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
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "store");

            // Order (R-2): schema → extensions → function → tables → raw-SQL indexes → sequence → view.
            // Extensions live in `public`, so `citext` and the trigram operators resolve whatever the search path puts first.
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS citext SCHEMA public;
                CREATE EXTENSION IF NOT EXISTS pg_trgm SCHEMA public;
                """);

            // Arabic-aware search: tashkeel and tatweel removed; alef forms → ا, taa marbuta → ه, alef maqsura → ي; lower-cased.
            migrationBuilder.Sql("""
                CREATE FUNCTION store.ar_normalize(t text) RETURNS text
                LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
                  SELECT lower(translate(
                           regexp_replace(coalesce(t, ''), '[\u064B-\u0652\u0640]', '', 'g'),
                           'أإآٱةى', 'ااااهي'))
                $$;
                """);

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    entity = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: true),
                    diff = table.Column<string>(type: "jsonb", nullable: true),
                    ip_hash = table.Column<string>(type: "character(64)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mobile = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "citext", nullable: true),
                    locale = table.Column<string>(type: "character(2)", nullable: false),
                    marketing_opt_in = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    pdpl_consent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer", x => x.id);
                    table.CheckConstraint("ck_customer_locale", "locale IN ('ar', 'en')");
                    table.CheckConstraint("ck_customer_mobile", "mobile ~ '^\\+9665[0-9]{8}$'");
                    table.CheckConstraint("ck_customer_status", "status IN ('active', 'blocked', 'anonymised')");
                });

            migrationBuilder.CreateTable(
                name: "idempotency_key",
                schema: "store",
                columns: table => new
                {
                    scope = table.Column<string>(type: "text", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    request_hash = table.Column<string>(type: "character(64)", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    response_status = table.Column<int>(type: "integer", nullable: true),
                    response_body = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_key", x => new { x.scope, x.key });
                    table.CheckConstraint("ck_idempotency_status", "status IN ('in_progress', 'completed')");
                });

            migrationBuilder.CreateTable(
                name: "media_asset",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    storage_key = table.Column<string>(type: "text", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    mime = table.Column<string>(type: "text", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    alt_ar = table.Column<string>(type: "text", nullable: true),
                    alt_en = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_asset", x => x.id);
                    table.CheckConstraint("ck_media_alt", "kind <> 'image' OR alt_ar IS NOT NULL");
                    table.CheckConstraint("ck_media_kind", "kind IN ('image', 'pdf')");
                    table.CheckConstraint("ck_media_size", "size_bytes > 0");
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
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_template", x => new { x.key, x.channel, x.locale });
                    table.CheckConstraint("ck_template_channel", "channel IN ('sms', 'email')");
                    table.CheckConstraint("ck_template_locale", "locale IN ('ar', 'en')");
                });

            migrationBuilder.CreateTable(
                name: "notification",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: false),
                    recipient_hash = table.Column<string>(type: "character(64)", nullable: false),
                    template_key = table.Column<string>(type: "text", nullable: false),
                    dedupe_key = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    provider_ref = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification", x => x.id);
                    table.CheckConstraint("ck_notification_channel", "channel IN ('sms', 'email')");
                    table.CheckConstraint("ck_notification_status", "status IN ('sent', 'failed')");
                });

            migrationBuilder.CreateTable(
                name: "otp_challenge",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mobile = table.Column<string>(type: "text", nullable: false),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    code_hash = table.Column<string>(type: "character(64)", nullable: false),
                    attempts = table.Column<short>(type: "smallint", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ip_hash = table.Column<string>(type: "character(64)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otp_challenge", x => x.id);
                    table.CheckConstraint("ck_otp_attempts", "attempts BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_otp_purpose", "purpose IN ('login')");
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    claimed_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    dead_lettered = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_token",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_type = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by = table.Column<Guid>(type: "uuid", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_token", x => x.id);
                    table.CheckConstraint("ck_refresh_expiry", "expires_at <= family_expires_at");
                    table.CheckConstraint("ck_refresh_subject", "subject_type IN ('customer', 'staff')");
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
                    table.PrimaryKey("pk_setting", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "staff_user",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "citext", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    pass_hash = table.Column<string>(type: "text", nullable: false),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    password_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    totp_secret_enc = table.Column<byte[]>(type: "bytea", nullable: true),
                    totp_last_step = table.Column<long>(type: "bigint", nullable: true),
                    roles = table.Column<string[]>(type: "text[]", nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    first_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_user", x => x.id);
                    table.CheckConstraint("ck_staff_roles", "cardinality(roles) > 0 AND roles <@ ARRAY['admin', 'store_manager', 'finance', 'viewer']::text[]");
                });

            migrationBuilder.CreateTable(
                name: "cart",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cart", x => x.id);
                    table.ForeignKey(
                        name: "fk_cart_customer",
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "text", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    donor_name = table.Column<string>(type: "text", nullable: false),
                    donor_email = table.Column<string>(type: "citext", nullable: false),
                    donor_mobile = table.Column<string>(type: "text", nullable: false),
                    locale = table.Column<string>(type: "character(2)", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    line_count = table.Column<int>(type: "integer", nullable: false),
                    status_token_hash = table.Column<string>(type: "character(64)", nullable: false),
                    payment_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_locale", "locale IN ('ar', 'en')");
                    table.CheckConstraint("ck_orders_reference", "reference ~ '^T3-[0-9]{2}-[0-9]{6,}$'");
                    table.CheckConstraint("ck_orders_status", "status IN ('pending_payment', 'paid', 'payment_review', 'cancelled', 'expired')");
                    table.CheckConstraint("ck_orders_total", "total_amount > 0 AND line_count > 0");
                    table.ForeignKey(
                        name: "fk_orders_customer",
                        column: x => x.customer_id,
                        principalSchema: "store",
                        principalTable: "customer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "category",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name_ar = table.Column<string>(type: "text", nullable: false),
                    name_en = table.Column<string>(type: "text", nullable: true),
                    description_ar = table.Column<string>(type: "text", nullable: true),
                    description_en = table.Column<string>(type: "text", nullable: true),
                    image_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accepts_general_gifts = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category", x => x.id);
                    table.CheckConstraint("ck_category_slug", "slug ~ '^[a-z0-9-]{2,60}$'");
                    table.ForeignKey(
                        name: "fk_category_image",
                        column: x => x.image_asset_id,
                        principalSchema: "store",
                        principalTable: "media_asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    title_ar = table.Column<string>(type: "text", nullable: false),
                    title_en = table.Column<string>(type: "text", nullable: true),
                    summary_ar = table.Column<string>(type: "text", nullable: true),
                    summary_en = table.Column<string>(type: "text", nullable: true),
                    body_ar = table.Column<string>(type: "text", nullable: true),
                    body_en = table.Column<string>(type: "text", nullable: true),
                    cover_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allow_open_amount = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    min_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    badge = table.Column<string>(type: "text", nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    seo_title_ar = table.Column<string>(type: "text", nullable: true),
                    seo_title_en = table.Column<string>(type: "text", nullable: true),
                    seo_desc_ar = table.Column<string>(type: "text", nullable: true),
                    seo_desc_en = table.Column<string>(type: "text", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('simple', store.ar_normalize(coalesce(title_ar, '') || ' ' || coalesce(title_en, '') || ' ' || coalesce(summary_ar, '') || ' ' || coalesce(summary_en, '')))", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project", x => x.id);
                    table.CheckConstraint("ck_project_badge", "badge IS NULL OR badge IN ('new', 'urgent')");
                    table.CheckConstraint("ck_project_min", "min_amount IS NULL OR min_amount > 0");
                    table.CheckConstraint("ck_project_slug", "slug ~ '^[a-z0-9-]{2,80}$'");
                    table.CheckConstraint("ck_project_status", "status IN ('draft', 'published', 'archived')");
                    table.CheckConstraint("ck_project_summary", "summary_ar IS NULL OR char_length(summary_ar) <= 300");
                    table.CheckConstraint("ck_project_title", "char_length(title_ar) BETWEEN 2 AND 160");
                    table.ForeignKey(
                        name: "fk_project_category",
                        column: x => x.category_id,
                        principalSchema: "store",
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_cover",
                        column: x => x.cover_asset_id,
                        principalSchema: "store",
                        principalTable: "media_asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_option",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label_ar = table.Column<string>(type: "text", nullable: false),
                    label_en = table.Column<string>(type: "text", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_option", x => x.id);
                    table.CheckConstraint("ck_option_amount", "amount > 0");
                    table.CheckConstraint("ck_option_label", "char_length(label_ar) BETWEEN 2 AND 60");
                    table.ForeignKey(
                        name: "fk_option_project",
                        column: x => x.project_id,
                        principalSchema: "store",
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_slug_history",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_slug = table.Column<string>(type: "text", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_slug_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_slug_history_project",
                        column: x => x.project_id,
                        principalSchema: "store",
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cart_line",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cart_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    option_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cart_line", x => x.id);
                    table.CheckConstraint("ck_line_amount", "amount = unit_amount * quantity AND quantity BETWEEN 1 AND 999 AND unit_amount > 0");
                    table.CheckConstraint("ck_line_kind", "target_kind IN ('project', 'category')");
                    table.CheckConstraint("ck_line_target", "(target_kind = 'project' AND project_id IS NOT NULL AND category_id IS NULL) OR (target_kind = 'category' AND category_id IS NOT NULL AND project_id IS NULL AND option_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_cart_line_cart",
                        column: x => x.cart_id,
                        principalSchema: "store",
                        principalTable: "cart",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cart_line_category",
                        column: x => x.category_id,
                        principalSchema: "store",
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cart_line_option",
                        column: x => x.option_id,
                        principalSchema: "store",
                        principalTable: "project_option",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cart_line_project",
                        column: x => x.project_id,
                        principalSchema: "store",
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_line",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<short>(type: "smallint", nullable: false),
                    target_kind = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    option_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title_ar = table.Column<string>(type: "text", nullable: false),
                    title_en = table.Column<string>(type: "text", nullable: true),
                    option_label_ar = table.Column<string>(type: "text", nullable: true),
                    option_label_en = table.Column<string>(type: "text", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_line", x => x.id);
                    table.CheckConstraint("ck_line_amount", "amount = unit_amount * quantity AND quantity BETWEEN 1 AND 999 AND unit_amount > 0");
                    table.CheckConstraint("ck_line_kind", "target_kind IN ('project', 'category')");
                    table.CheckConstraint("ck_line_target", "(target_kind = 'project' AND project_id IS NOT NULL AND category_id IS NULL) OR (target_kind = 'category' AND category_id IS NOT NULL AND project_id IS NULL AND option_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_order_line_category",
                        column: x => x.category_id,
                        principalSchema: "store",
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_order_line_option",
                        column: x => x.option_id,
                        principalSchema: "store",
                        principalTable: "project_option",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_order_line_order",
                        column: x => x.order_id,
                        principalSchema: "store",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_order_line_project",
                        column: x => x.project_id,
                        principalSchema: "store",
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_action",
                schema: "store",
                table: "audit_log",
                column: "action");

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
                name: "ix_cart_guest_expiry",
                schema: "store",
                table: "cart",
                column: "expires_at",
                filter: "customer_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cart_line_cart",
                schema: "store",
                table: "cart_line",
                column: "cart_id");

            migrationBuilder.CreateIndex(
                name: "ix_cart_line_category",
                schema: "store",
                table: "cart_line",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_cart_line_option",
                schema: "store",
                table: "cart_line",
                column: "option_id");

            migrationBuilder.CreateIndex(
                name: "ix_cart_line_project",
                schema: "store",
                table: "cart_line",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_category_image",
                schema: "store",
                table: "category",
                column: "image_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_category_sort",
                schema: "store",
                table: "category",
                columns: new[] { "is_active", "sort" });

            migrationBuilder.CreateIndex(
                name: "ux_category_slug",
                schema: "store",
                table: "category",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_email",
                schema: "store",
                table: "customer",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ux_customer_mobile",
                schema: "store",
                table: "customer",
                column: "mobile",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_expires",
                schema: "store",
                table: "idempotency_key",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_media_created",
                schema: "store",
                table: "media_asset",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ux_media_storage_key",
                schema: "store",
                table: "media_asset",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_created",
                schema: "store",
                table: "notification",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ux_notification_dedupe",
                schema: "store",
                table: "notification",
                column: "dedupe_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_line_category",
                schema: "store",
                table: "order_line",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_line_option",
                schema: "store",
                table: "order_line",
                column: "option_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_line_project",
                schema: "store",
                table: "order_line",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ux_order_line_no",
                schema: "store",
                table: "order_line",
                columns: new[] { "order_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer",
                schema: "store",
                table: "orders",
                columns: new[] { "customer_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_orders_status",
                schema: "store",
                table: "orders",
                columns: new[] { "status", "payment_due_at" });

            migrationBuilder.CreateIndex(
                name: "ux_orders_reference",
                schema: "store",
                table: "orders",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_otp_created",
                schema: "store",
                table: "otp_challenge",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_otp_ip_recent",
                schema: "store",
                table: "otp_challenge",
                columns: new[] { "ip_hash", "created_at" },
                descending: new[] { false, true });

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
                filter: "processed_at IS NULL AND NOT dead_lettered");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_type",
                schema: "store",
                table: "outbox_message",
                columns: new[] { "type", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_project_category",
                schema: "store",
                table: "project",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_cover",
                schema: "store",
                table: "project",
                column: "cover_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_listing",
                schema: "store",
                table: "project",
                columns: new[] { "status", "category_id", "sort" });

            migrationBuilder.CreateIndex(
                name: "ux_project_slug",
                schema: "store",
                table: "project",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_option_label",
                schema: "store",
                table: "project_option",
                columns: new[] { "project_id", "label_ar" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_slug_history_project",
                schema: "store",
                table: "project_slug_history",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ux_slug_history_old",
                schema: "store",
                table: "project_slug_history",
                column: "old_slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_family",
                schema: "store",
                table: "refresh_token",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_subject",
                schema: "store",
                table: "refresh_token",
                columns: new[] { "subject_type", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ux_refresh_token_hash",
                schema: "store",
                table: "refresh_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_staff_email",
                schema: "store",
                table: "staff_user",
                column: "email",
                unique: true);

            migrationBuilder.Sql("""
                CREATE INDEX ix_project_search ON store.project USING gin (search_vector);
                CREATE INDEX ix_project_title_trgm ON store.project USING gin (store.ar_normalize(title_ar) public.gin_trgm_ops);
                CREATE UNIQUE INDEX ux_cart_customer ON store.cart (customer_id) WHERE customer_id IS NOT NULL;
                CREATE UNIQUE INDEX ux_cart_option_line ON store.cart_line (cart_id, option_id) WHERE option_id IS NOT NULL;
                """);

            migrationBuilder.CreateSequence(
                name: "order_ref_seq",
                schema: "store");

            // Staff dashboard: pending donation orders and their amount per project and per category.
            migrationBuilder.Sql("""
                CREATE VIEW store.v_catalog_stats AS
                SELECT l.target_kind,
                       coalesce(l.project_id, l.category_id) AS target_id,
                       count(DISTINCT o.id)::int AS pending_orders,
                       sum(l.amount)::numeric(12,2) AS pending_amount
                  FROM store.order_line l
                  JOIN store.orders o ON o.id = l.order_id
                 WHERE o.status = 'pending_payment'
                 GROUP BY l.target_kind, coalesce(l.project_id, l.category_id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS store.v_catalog_stats;");

            migrationBuilder.DropSequence(
                name: "order_ref_seq",
                schema: "store");

            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "store");

            migrationBuilder.DropTable(
                name: "cart_line",
                schema: "store");

            migrationBuilder.DropTable(
                name: "idempotency_key",
                schema: "store");

            migrationBuilder.DropTable(
                name: "message_template",
                schema: "store");

            migrationBuilder.DropTable(
                name: "notification",
                schema: "store");

            migrationBuilder.DropTable(
                name: "order_line",
                schema: "store");

            migrationBuilder.DropTable(
                name: "otp_challenge",
                schema: "store");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "store");

            migrationBuilder.DropTable(
                name: "project_slug_history",
                schema: "store");

            migrationBuilder.DropTable(
                name: "refresh_token",
                schema: "store");

            migrationBuilder.DropTable(
                name: "setting",
                schema: "store");

            migrationBuilder.DropTable(
                name: "staff_user",
                schema: "store");

            migrationBuilder.DropTable(
                name: "cart",
                schema: "store");

            migrationBuilder.DropTable(
                name: "project_option",
                schema: "store");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "store");

            migrationBuilder.DropTable(
                name: "project",
                schema: "store");

            migrationBuilder.DropTable(
                name: "customer",
                schema: "store");

            migrationBuilder.DropTable(
                name: "category",
                schema: "store");

            migrationBuilder.DropTable(
                name: "media_asset",
                schema: "store");

            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS store.ar_normalize(text);
                DROP EXTENSION IF EXISTS pg_trgm;
                DROP EXTENSION IF EXISTS citext;
                """);
        }
    }
}
