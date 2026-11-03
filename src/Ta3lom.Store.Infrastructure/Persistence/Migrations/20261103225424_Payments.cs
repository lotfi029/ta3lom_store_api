using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ta3lom.Store.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_status",
                schema: "store",
                table: "orders");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expired_at",
                schema: "store",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "refunded_amount",
                schema: "store",
                table: "orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refunded_at",
                schema: "store",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_reason",
                schema: "store",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "document_counter",
                schema: "store",
                columns: table => new
                {
                    series = table.Column<string>(type: "text", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    next_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_counter", x => new { x.series, x.year });
                    table.CheckConstraint("ck_document_counter_next", "next_number >= 1");
                });

            migrationBuilder.CreateTable(
                name: "donation_receipt",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    series = table.Column<string>(type: "text", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    donor_name_snapshot = table.Column<string>(type: "text", nullable: false),
                    lines_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    pdf_storage_key = table.Column<string>(type: "text", nullable: true),
                    voided_by_receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    voids_receipt_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_donation_receipt", x => x.id);
                    table.CheckConstraint("ck_receipt_number", "number > 0 AND year BETWEEN 2000 AND 2999");
                    table.CheckConstraint("ck_receipt_series", "series IN ('DR', 'DRC')");
                    table.CheckConstraint("ck_receipt_voids", "(series = 'DRC') = (voids_receipt_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_receipt_order",
                        column: x => x.order_id,
                        principalSchema: "store",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_receipt_voids",
                        column: x => x.voids_receipt_id,
                        principalSchema: "store",
                        principalTable: "donation_receipt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_no = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    provider_payment_id = table.Column<string>(type: "text", nullable: true),
                    provider_invoice_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    redirect_url = table.Column<string>(type: "text", nullable: true),
                    redirect_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "text", nullable: true),
                    failure_message = table.Column<string>(type: "text", nullable: true),
                    captured_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    refunded_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    review_reason = table.Column<string>(type: "text", nullable: true),
                    initiated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment", x => x.id);
                    table.CheckConstraint("ck_payment_amounts", "amount > 0 AND captured_amount >= 0 AND refunded_amount >= 0 AND refunded_amount <= captured_amount");
                    table.CheckConstraint("ck_payment_attempt", "attempt_no >= 1");
                    table.CheckConstraint("ck_payment_currency", "currency = 'SAR'");
                    table.CheckConstraint("ck_payment_method", "method IN ('unknown', 'mada', 'creditcard', 'applepay', 'stcpay')");
                    table.CheckConstraint("ck_payment_review_reason", "review_reason IS NULL OR review_reason IN ('amount_mismatch', 'late_capture', 'double_payment')");
                    table.CheckConstraint("ck_payment_status", "status IN ('initiated', 'captured', 'failed', 'cancelled', 'review', 'refunded')");
                    table.ForeignKey(
                        name: "fk_payment_order",
                        column: x => x.order_id,
                        principalSchema: "store",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_event",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    provider_event_id = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_payment_id = table.Column<string>(type: "text", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    signature_valid = table.Column<bool>(type: "boolean", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<string>(type: "text", nullable: true),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_event", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_event_payment",
                        column: x => x.payment_id,
                        principalSchema: "store",
                        principalTable: "payment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refund",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    provider_refund_id = table.Column<string>(type: "text", nullable: true),
                    failure_message = table.Column<string>(type: "text", nullable: true),
                    requested_by = table.Column<string>(type: "citext", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    idempotency_key = table.Column<string>(type: "text", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refund", x => x.id);
                    table.CheckConstraint("ck_refund_amount", "amount > 0");
                    table.CheckConstraint("ck_refund_status", "status IN ('requested', 'succeeded', 'failed')");
                    table.ForeignKey(
                        name: "fk_refund_order",
                        column: x => x.order_id,
                        principalSchema: "store",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_refund_payment",
                        column: x => x.payment_id,
                        principalSchema: "store",
                        principalTable: "payment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_refunded",
                schema: "store",
                table: "orders",
                sql: "refunded_amount >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_review_reason",
                schema: "store",
                table: "orders",
                sql: "review_reason IS NULL OR review_reason IN ('amount_mismatch', 'late_capture', 'double_payment')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_status",
                schema: "store",
                table: "orders",
                sql: "status IN ('pending_payment', 'paid', 'payment_review', 'cancelled', 'expired', 'refunded')");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_order",
                schema: "store",
                table: "donation_receipt",
                column: "order_id",
                unique: true,
                filter: "series = 'DR'");

            migrationBuilder.CreateIndex(
                name: "ux_receipt_number",
                schema: "store",
                table: "donation_receipt",
                columns: new[] { "series", "year", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_provider_invoice",
                schema: "store",
                table: "payment",
                columns: new[] { "provider", "provider_invoice_id" },
                filter: "provider_invoice_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_status",
                schema: "store",
                table: "payment",
                columns: new[] { "status", "initiated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_payment_attempt",
                schema: "store",
                table: "payment",
                columns: new[] { "order_id", "attempt_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_provider_id",
                schema: "store",
                table: "payment",
                columns: new[] { "provider", "provider_payment_id" },
                unique: true,
                filter: "provider_payment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_event_payment",
                schema: "store",
                table: "payment_event",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ux_payment_event_provider",
                schema: "store",
                table: "payment_event",
                columns: new[] { "provider", "provider_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refund_payment",
                schema: "store",
                table: "refund",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ux_refund_idempotency",
                schema: "store",
                table: "refund",
                columns: new[] { "order_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_counter",
                schema: "store");

            migrationBuilder.DropTable(
                name: "donation_receipt",
                schema: "store");

            migrationBuilder.DropTable(
                name: "payment_event",
                schema: "store");

            migrationBuilder.DropTable(
                name: "refund",
                schema: "store");

            migrationBuilder.DropTable(
                name: "payment",
                schema: "store");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_refunded",
                schema: "store",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_review_reason",
                schema: "store",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_status",
                schema: "store",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "expired_at",
                schema: "store",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "refunded_amount",
                schema: "store",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "refunded_at",
                schema: "store",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "review_reason",
                schema: "store",
                table: "orders");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_status",
                schema: "store",
                table: "orders",
                sql: "status IN ('pending_payment', 'paid', 'payment_review', 'cancelled', 'expired')");
        }
    }
}
