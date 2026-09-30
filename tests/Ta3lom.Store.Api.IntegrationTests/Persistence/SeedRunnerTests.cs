using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;
using Ta3lom.Store.Infrastructure.Persistence.Seeders;

namespace Ta3lom.Store.Api.IntegrationTests.Persistence;

/// SeedRunner inserts missing reference data only (database/03 §3), so it can run on every start.
public sealed class SeedRunnerTests
{
    [Fact]
    public async Task Seeds_settings_templates_and_counters_once()
    {
        var cs = await StoreSchema.CreateMigratedAsync("seed");

        var first = await StoreSchema.SeedAsync(cs);
        var second = await StoreSchema.SeedAsync(cs);

        first.ShouldBe(SeedData.Settings.Count + SeedData.Templates.Count + SeedData.DocumentSeries.Count);
        second.ShouldBe(0);
        (await SqlRunner.ScalarAsync<long>(cs, "SELECT count(*) FROM store.setting")).ShouldBe(SeedData.Settings.Count);
    }

    [Fact]
    public async Task Every_business_setting_of_03_is_seeded()
    {
        var cs = await StoreSchema.CreateMigratedAsync("seed_keys");
        await StoreSchema.SeedAsync(cs);

        var keys = (await SqlRunner.QueryAllAsync(cs, "SELECT key FROM store.setting ORDER BY key")).Single();

        // 03-business-rules.md §M
        string[] required = [
            "vat_enabled", "vat_rate", "shipping_flat_fee", "min_gift_amount", "cart_max_total",
            "order_payment_timeout_min", "download_limit", "allocation_alert_days", "allocation_escalate_days",
            "lapse_days", "refund_window_days", "refund_progress_max_pct", "low_stock_default_threshold",
            "gift_refund_window_hours", "dedication_free_text_enabled", "dedication_templates",
        ];
        required.Where(k => !keys.Contains(k)).ShouldBeEmpty();
        (await SqlRunner.ScalarAsync<string>(cs, "SELECT value::text FROM store.setting WHERE key = 'low_stock_default_threshold'")).ShouldBe("20");
    }

    [Fact]
    public async Task Document_counters_are_created_for_the_current_Riyadh_year()
    {
        var cs = await StoreSchema.CreateMigratedAsync("seed_year");
        var newYearsEveInRiyadh = new FakeClock(new DateTimeOffset(2027, 12, 31, 21, 30, 0, TimeSpan.Zero));   // 00:30 on 1 Jan 2028 in Riyadh

        await StoreSchema.SeedAsync(cs, newYearsEveInRiyadh);

        (await SqlRunner.QueryAllAsync(cs, "SELECT series || ':' || year || ':' || last_value FROM store.document_counter ORDER BY series"))
            .Single().ShouldBe(["CN:2028:0", "DR:2028:0", "DRC:2028:0", "INV:2028:0"]);
    }

    [Fact]
    public async Task Catalogue_seed_can_run_after_SeedRunner()
    {
        var cs = await StoreSchema.CreateMigratedAsync("seed_order");
        await StoreSchema.SeedAsync(cs);

        await SqlRunner.ExecuteAsync(cs, SqlRunner.ReferenceFile("002_store_seed.sql"));

        (await SqlRunner.ScalarAsync<long>(cs, "SELECT count(*) FROM store.product")).ShouldBe(15);
    }

    [Fact]
    public async Task Every_outbox_template_key_of_09_exists_in_both_languages()
    {
        string[] smsKeys = ["otp", "order_paid", "dedication_card", "beneficiary_invite", "beneficiary_lapsed",
                            "shipment_shipped", "shipment_delivered", "refund_done", "cause_update"];
        string[] emailKeys = ["order_paid", "refund_done", "cause_update", "staff_alert", "sponsor_digest"];

        var present = SeedData.Templates.Select(t => $"{t.Key}/{t.Channel}/{t.Locale}").ToHashSet(StringComparer.Ordinal);

        var expected = smsKeys.SelectMany(k => new[] { $"{k}/sms/ar", $"{k}/sms/en" })
            .Concat(emailKeys.SelectMany(k => new[] { $"{k}/email/ar", $"{k}/email/en" }));
        expected.Where(e => !present.Contains(e)).ShouldBeEmpty();
    }
}
