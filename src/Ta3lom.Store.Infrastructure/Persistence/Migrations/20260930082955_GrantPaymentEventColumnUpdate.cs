using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ta3lom.Store.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// database/01 §7 (r2, 30 Sep): store_app may UPDATE only processed_at, error and payload of payment_event.
    /// Settlement sets the first two; retention scrubs payload (03 §7). A missing role is skipped (roles: S-03).
    /// </summary>
    public partial class GrantPaymentEventColumnUpdate : Migration
    {
        internal const string Grant = @"
DO $grant$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'store_app') THEN
        GRANT UPDATE (processed_at, error, payload) ON store.payment_event TO store_app;
    END IF;
END
$grant$;";

        internal const string Revoke = @"
DO $revoke$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'store_app') THEN
        REVOKE UPDATE (processed_at, error, payload) ON store.payment_event FROM store_app;
    END IF;
END
$revoke$;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Grant);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Revoke);
        }
    }
}
