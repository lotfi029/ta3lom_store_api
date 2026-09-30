using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;
using Ta3lom.Store.Infrastructure.Persistence.Migrations;

namespace Ta3lom.Store.Api.IntegrationTests.Persistence;

/// database/01 §7: store_app has DML but only INSERT/SELECT on the append-only ledgers (payment_event also a
/// column-level UPDATE of processed_at, error, payload — r2); store_report reads views.
/// Roles are cluster-wide (created by operations in S-03); this test creates NOLOGIN roles if missing.
public sealed class GrantsTests
{
    [Fact]
    public async Task Grants_follow_the_least_privilege_design()
    {
        var cs = await StoreSchema.CreateMigratedAsync("grants");
        await SqlRunner.ExecuteAsync(cs, """
            DO $$ BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'store_owner') THEN CREATE ROLE store_owner NOLOGIN; END IF;
              IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'store_app') THEN CREATE ROLE store_app NOLOGIN; END IF;
              IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'store_report') THEN CREATE ROLE store_report NOLOGIN; END IF;
            END $$;
            """);

        await SqlRunner.ExecuteAsync(cs, InitialSchema.Grants);
        await SqlRunner.ExecuteAsync(cs, GrantPaymentEventColumnUpdate.Grant);

        async Task<bool> Can(string role, string table, string privilege) =>
            await SqlRunner.ScalarAsync<bool>(cs, $"SELECT has_table_privilege('{role}', 'store.{table}', '{privilege}')");

        (await Can("store_app", "orders", "UPDATE")).ShouldBeTrue();
        (await Can("store_app", "cart_line", "DELETE")).ShouldBeTrue();
        foreach (var ledger in new[] { "audit_log", "payment_event", "zakat_ledger", "stock_movement", "sponsored_unit_event" })
        {
            (await Can("store_app", ledger, "INSERT")).ShouldBeTrue(ledger);
            (await Can("store_app", ledger, "SELECT")).ShouldBeTrue(ledger);
            (await Can("store_app", ledger, "UPDATE")).ShouldBeFalse(ledger);
            (await Can("store_app", ledger, "DELETE")).ShouldBeFalse(ledger);
        }

        // payment_event (r2): column-level UPDATE of processed_at, error, payload only; DELETE stays revoked.
        async Task<bool> CanUpdateColumn(string column) =>
            await SqlRunner.ScalarAsync<bool>(cs, $"SELECT has_column_privilege('store_app', 'store.payment_event', '{column}', 'UPDATE')");
        foreach (var column in new[] { "processed_at", "error", "payload" })
        {
            (await CanUpdateColumn(column)).ShouldBeTrue(column);
        }

        foreach (var column in new[] { "id", "provider", "provider_event_id", "event_type", "signature_valid", "received_at" })
        {
            (await CanUpdateColumn(column)).ShouldBeFalse(column);
        }

        (await Can("store_report", "v_goal", "SELECT")).ShouldBeTrue();
        (await Can("store_report", "orders", "SELECT")).ShouldBeFalse();
        (await SqlRunner.ScalarAsync<bool>(cs, "SELECT has_sequence_privilege('store_app', 'store.order_ref_seq', 'USAGE')")).ShouldBeTrue();
    }
}
