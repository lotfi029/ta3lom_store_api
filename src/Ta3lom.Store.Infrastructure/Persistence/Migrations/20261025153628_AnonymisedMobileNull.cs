using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ta3lom.Store.Infrastructure.Persistence.Migrations
{
    /// L-5: an anonymised donor keeps no mobile (NULL), so the number can sign up again. Order: drop the unique index →
    /// DROP NOT NULL → clear the placeholders of anonymised rows → filtered unique index → CHECK (blocked donors keep their
    /// mobile). Down restores placeholders and leaves the extensions in place.
    public partial class AnonymisedMobileNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_customer_mobile",
                schema: "store",
                table: "customer");

            migrationBuilder.AlterColumn<string>(
                name: "mobile",
                schema: "store",
                table: "customer",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.Sql("UPDATE store.customer SET mobile = NULL WHERE status = 'anonymised';");

            migrationBuilder.CreateIndex(
                name: "ux_customer_mobile",
                schema: "store",
                table: "customer",
                column: "mobile",
                unique: true,
                filter: "mobile IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_mobile_present",
                schema: "store",
                table: "customer",
                sql: "status = 'anonymised' OR mobile IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_customer_mobile",
                schema: "store",
                table: "customer");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_mobile_present",
                schema: "store",
                table: "customer");

            migrationBuilder.Sql("""
                UPDATE store.customer
                   SET mobile = '+9665' || lpad((abs(hashtextextended(id::text, 0)) % 100000000)::text, 8, '0')
                 WHERE mobile IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "mobile",
                schema: "store",
                table: "customer",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_customer_mobile",
                schema: "store",
                table: "customer",
                column: "mobile",
                unique: true);
        }
    }
}
