using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;

namespace Ta3lom.Store.Api.IntegrationTests.Persistence;

/// db/reference scripts run unchanged against the migrated schema: the catalogue seed, SeedRunner, the smoke
/// scenario (mixed order, settlement effects, allocation, two negative tests) and the nine integrity checks.
public sealed class ReferenceScriptsTests
{
    [Theory]
    [InlineData("migrations")]
    [InlineData("reference")]
    public async Task Seed_smoke_scenario_and_integrity_checks_pass(string builtFrom)
    {
        var cs = builtFrom == "migrations"
            ? await StoreSchema.CreateMigratedAsync("smoke")
            : await StoreSchema.CreateFromReferenceAsync("smoke_ref");

        await SqlRunner.ExecuteAsync(cs, SqlRunner.ReferenceFile("002_store_seed.sql"));
        await StoreSchema.SeedAsync(cs);
        await SqlRunner.QueryAllAsync(cs, SqlRunner.ReferenceFile("tests/smoke_scenario.sql"));   // raises on a failed negative test
        var checks = await SqlRunner.QueryAllAsync(cs, SqlRunner.ReferenceFile("003_integrity_checks.sql"));

        (await SqlRunner.ScalarAsync<long>(cs, "SELECT count(*) FROM store.product WHERE status = 'published'")).ShouldBe(15);
        checks.Count.ShouldBe(10);   // nine checks, check 8 has two queries
        checks.SelectMany((rows, i) => rows.Select(r => $"check query {i + 1}: {r}")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Integrity_checks_detect_a_broken_stock_counter()
    {
        var cs = await StoreSchema.CreateMigratedAsync("broken");
        await SqlRunner.ExecuteAsync(cs, SqlRunner.ReferenceFile("002_store_seed.sql"));
        await SqlRunner.ExecuteAsync(cs, "UPDATE store.book_format SET stock_on_hand = stock_on_hand + 1 WHERE sku = 'BK-SCIENCE-STAR-P'");

        var checks = await SqlRunner.QueryAllAsync(cs, SqlRunner.ReferenceFile("003_integrity_checks.sql"));

        checks[2].ShouldBe(["BK-SCIENCE-STAR-P"]);   // check 3: stock counters match the ledger
    }
}
