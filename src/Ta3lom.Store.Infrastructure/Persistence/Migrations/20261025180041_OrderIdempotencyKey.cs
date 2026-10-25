using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ta3lom.Store.Infrastructure.Persistence.Migrations
{
    /// A-5: the order keeps the checkout's `Idempotency-Key` (nullable: older orders have none), unique per donor, so a
    /// retry whose stored response was lost replays the order. Expand only.
    public partial class OrderIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                schema: "store",
                table: "orders",
                type: "text",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_orders_idempotency",
                schema: "store",
                table: "orders",
                columns: new[] { "customer_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_orders_idempotency",
                schema: "store",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                schema: "store",
                table: "orders");
        }
    }
}
