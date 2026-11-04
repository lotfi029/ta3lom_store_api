using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ta3lom.Store.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentsHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_refund_idempotency",
                schema: "store",
                table: "refund");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_review_reason",
                schema: "store",
                table: "payment");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_review_reason",
                schema: "store",
                table: "orders");

            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "store",
                table: "refund",
                type: "text",
                nullable: false,
                defaultValueSql: "'store'");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "claimed_at",
                schema: "store",
                table: "payment_event",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "gateway_amount",
                schema: "store",
                table: "payment_event",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gateway_currency",
                schema: "store",
                table: "payment_event",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_invoice_id",
                schema: "store",
                table: "payment_event",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_note",
                schema: "store",
                table: "payment_event",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resolved_at",
                schema: "store",
                table: "payment_event",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolved_by",
                schema: "store",
                table: "payment_event",
                type: "citext",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_refund_idempotency",
                schema: "store",
                table: "refund",
                columns: new[] { "order_id", "requested_by", "idempotency_key" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_refund_source",
                schema: "store",
                table: "refund",
                sql: "source IN ('store', 'gateway')");

            migrationBuilder.CreateIndex(
                name: "ix_payment_event_orphan",
                schema: "store",
                table: "payment_event",
                column: "outcome",
                filter: "outcome = 'orphan'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_review_reason",
                schema: "store",
                table: "payment",
                sql: "review_reason IS NULL OR review_reason IN ('amount_mismatch', 'late_capture', 'double_payment', 'external_refund', 'voided_after_capture')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_review_reason",
                schema: "store",
                table: "orders",
                sql: "review_reason IS NULL OR review_reason IN ('amount_mismatch', 'late_capture', 'double_payment', 'external_refund', 'voided_after_capture')");

            // Review 04 A-6: the refund pending timeout (minutes). The seeder does not run in production: the row comes here.
            migrationBuilder.Sql("""
                INSERT INTO store.setting (key, value, updated_at) VALUES ('refund_pending_timeout_minutes', '30'::jsonb, now())
                ON CONFLICT (key) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM store.setting WHERE key = 'refund_pending_timeout_minutes';");

            migrationBuilder.DropIndex(
                name: "ux_refund_idempotency",
                schema: "store",
                table: "refund");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refund_source",
                schema: "store",
                table: "refund");

            migrationBuilder.DropIndex(
                name: "ix_payment_event_orphan",
                schema: "store",
                table: "payment_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_review_reason",
                schema: "store",
                table: "payment");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_review_reason",
                schema: "store",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "store",
                table: "refund");

            migrationBuilder.DropColumn(
                name: "claimed_at",
                schema: "store",
                table: "payment_event");

            migrationBuilder.DropColumn(
                name: "gateway_amount",
                schema: "store",
                table: "payment_event");

            migrationBuilder.DropColumn(
                name: "gateway_currency",
                schema: "store",
                table: "payment_event");

            migrationBuilder.DropColumn(
                name: "provider_invoice_id",
                schema: "store",
                table: "payment_event");

            migrationBuilder.DropColumn(
                name: "resolution_note",
                schema: "store",
                table: "payment_event");

            migrationBuilder.DropColumn(
                name: "resolved_at",
                schema: "store",
                table: "payment_event");

            migrationBuilder.DropColumn(
                name: "resolved_by",
                schema: "store",
                table: "payment_event");

            migrationBuilder.CreateIndex(
                name: "ux_refund_idempotency",
                schema: "store",
                table: "refund",
                columns: new[] { "order_id", "idempotency_key" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_review_reason",
                schema: "store",
                table: "payment",
                sql: "review_reason IS NULL OR review_reason IN ('amount_mismatch', 'late_capture', 'double_payment')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_review_reason",
                schema: "store",
                table: "orders",
                sql: "review_reason IS NULL OR review_reason IN ('amount_mismatch', 'late_capture', 'double_payment')");
        }
    }
}
