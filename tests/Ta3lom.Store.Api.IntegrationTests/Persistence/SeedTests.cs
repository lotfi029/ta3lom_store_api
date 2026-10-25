using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Infrastructure.Persistence.Seeders;

namespace Ta3lom.Store.Api.IntegrationTests.Persistence;

/// SeedRunner inserts missing reference data only, so it runs on every start.
public sealed class SeedRunnerTests
{
    [Fact]
    public async Task Seeds_settings_and_templates_once()
    {
        var cs = await StoreSchema.CreateMigratedAsync("seed");

        var first = await StoreSchema.SeedAsync(cs);
        var second = await StoreSchema.SeedAsync(cs);

        first.ShouldBe(SeedData.Settings.Count + SeedData.Templates.Count);
        second.ShouldBe(0);
    }

    [Fact]
    public async Task Every_setting_key_is_seeded_with_its_default()
    {
        var cs = await StoreSchema.CreateMigratedAsync("seed_keys");
        await StoreSchema.SeedAsync(cs);

        var keys = (await SqlRunner.QueryAllAsync(cs, "SELECT key FROM store.setting ORDER BY key")).Single();

        keys.ShouldBe(SettingKeys.All.Order(StringComparer.Ordinal).ToList());
        (await SqlRunner.ScalarAsync<string>(cs, "SELECT value::text FROM store.setting WHERE key = 'min_gift_amount'")).ShouldBe("\"10.00\"");
        (await SqlRunner.ScalarAsync<string>(cs, "SELECT value::text FROM store.setting WHERE key = 'cart_max_lines'")).ShouldBe("50");
        (await SqlRunner.ScalarAsync<string>(cs, "SELECT value::text FROM store.setting WHERE key = 'amount_presets'")).ShouldBe("[\"50.00\", \"100.00\", \"500.00\"]");
    }

    [Fact]
    public void The_OTP_and_staff_alert_templates_exist_in_both_languages() =>
        SeedData.Templates.Select(t => $"{t.Key}/{t.Channel}/{t.Locale}")
            .ShouldBe(["otp/sms/ar", "otp/sms/en", "staff_alert/email/ar", "staff_alert/email/en"], ignoreOrder: true);
}

/// The Development demo catalogue: 3 categories, 6 published projects, real WebP placeholders; safe to run twice.
public sealed class DevelopmentCatalogSeederTests(DevelopmentSeedFactory factory) : IClassFixture<DevelopmentSeedFactory>
{
    [Fact]
    public async Task The_demo_catalogue_is_seeded_once_with_real_images()
    {
        var first = await RunAsync();
        var second = await RunAsync();

        first.ShouldBe(9);                                                        // 3 categories + 6 projects
        second.ShouldBe(0);
        (await factory.IntegrityFailuresAsync()).ShouldBeEmpty();
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.category")).ShouldBe(3);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.project WHERE status = 'published'")).ShouldBe(6);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.media_asset WHERE kind = 'image' AND mime = 'image/webp'")).ShouldBe(9);

        var options = (await SqlRunner.QueryAllAsync(factory.ConnectionStringValue, """
            SELECT p.slug || ':' || p.allow_open_amount || ':' || coalesce(string_agg(o.label_ar || '=' || o.amount, ',' ORDER BY o.sort), '')
              FROM store.project p LEFT JOIN store.project_option o ON o.project_id = p.id GROUP BY p.slug, p.allow_open_amount ORDER BY p.slug
            """)).Single();
        options.ShouldBe([
            "dawah-umrah:true:تكلفة المعتمر=150.00",
            "iftar-meals:true:",
            "learner-support:true:",
            "print-a-title:true:نسخة عربية=4.00,نسخة مترجمة=5.00",
            "support-a-daiya:false:ساعة=25.00,يوم=100.00,أسبوع=500.00,شهر=2000.00",
            "water-wells:true:",
        ]);

        await using var file = await factory.Services.GetRequiredService<IFileStorage>().OpenReadAsync("seed/project-dawah-umrah.webp", isPublic: true, TestContext.Current.CancellationToken);
        file.ShouldNotBeNull();
        var head = new byte[12];
        await file.ReadExactlyAsync(head, TestContext.Current.CancellationToken);
        System.Text.Encoding.ASCII.GetString(head, 8, 4).ShouldBe("WEBP");
    }

    private async Task<int> RunAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DevelopmentCatalogSeeder>().RunAsync(TestContext.Current.CancellationToken);
    }
}

/// A host with its own database so the demo catalogue does not leak into other tests.
public sealed class DevelopmentSeedFactory : Infrastructure.Scenario.ScenarioFactory
{
    public string ConnectionStringValue => DatabaseConnectionString;
}

/// The integrity checks find a broken order total.
public sealed class IntegrityChecksTests(Infrastructure.Scenario.ScenarioFactory factory) : IClassFixture<Infrastructure.Scenario.ScenarioFactory>
{
    [Fact]
    public async Task A_clean_database_passes_and_a_wrong_order_total_fails_check_1()
    {
        (await factory.IntegrityFailuresAsync()).ShouldBeEmpty();
        var customer = Guid.CreateVersion7();
        var category = Guid.CreateVersion7();
        await factory.ExecuteSqlAsync($"""
            INSERT INTO store.customer (id, mobile, locale, marketing_opt_in, status, created_at, updated_at) VALUES ('{customer}', '+966500000001', 'ar', false, 'active', now(), now());
            INSERT INTO store.category (id, slug, name_ar, accepts_general_gifts, sort, is_active, created_at, updated_at) VALUES ('{category}', 'check-cat', 'فئة', true, 1, true, now(), now());
            INSERT INTO store.orders (id, reference, customer_id, status, donor_name, donor_email, donor_mobile, locale, total_amount, line_count, status_token_hash, payment_due_at, created_at, updated_at)
            VALUES ('{Guid.CreateVersion7()}', 'T3-26-999999', '{customer}', 'pending_payment', 'x', 'x@example.com', '+966500000001', 'ar', 99, 1, repeat('a', 64), now() + interval '1 hour', now(), now());
            INSERT INTO store.order_line (id, order_id, line_no, target_kind, category_id, title_ar, quantity, unit_amount, amount)
            SELECT '{Guid.CreateVersion7()}', id, 1, 'category', '{category}', 'فئة', 1, 50, 50 FROM store.orders WHERE reference = 'T3-26-999999';
            """);

        var failures = await factory.IntegrityFailuresAsync();

        failures.ShouldHaveSingleItem().ShouldStartWith("1 ");
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IOperationsStatus>().RunIntegrityChecksAsync(TestContext.Current.CancellationToken)).Count.ShouldBe(4);
        await factory.ExecuteSqlAsync("DELETE FROM store.orders WHERE reference = 'T3-26-999999'");
    }
}
