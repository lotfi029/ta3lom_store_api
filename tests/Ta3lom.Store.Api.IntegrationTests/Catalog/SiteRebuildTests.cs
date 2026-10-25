using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.Catalog;

/// Records the rebuilds instead of calling GitHub.
public sealed class SiteRebuildFactory : ScenarioFactory
{
    public ConcurrentQueue<string> Rebuilds { get; } = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.RemoveAll<ISiteRebuildTrigger>();
        services.AddSingleton<ISiteRebuildTrigger>(new RecordingTrigger(Rebuilds));
    }

    private sealed class RecordingTrigger(ConcurrentQueue<string> rebuilds) : ISiteRebuildTrigger
    {
        public Task<bool> TriggerAsync(string reason, CancellationToken ct = default)
        {
            rebuilds.Enqueue(reason);
            return Task.FromResult(true);
        }
    }
}

/// Every catalogue write queues `CatalogChanged`; a burst is one rebuild (unclaimed rows are absorbed, only the newest
/// pending change triggers) and a row another dispatcher has claimed is never absorbed.
public sealed class SiteRebuildTests(SiteRebuildFactory factory) : IClassFixture<SiteRebuildFactory>
{
    private const string Pending = "SELECT count(*) FROM store.outbox_message WHERE type = 'CatalogChanged' AND processed_at IS NULL";

    [Fact]
    public async Task A_burst_of_writes_is_one_rebuild_and_claimed_rows_are_left_alone()
    {
        await ResetAsync();
        var build = new CatalogBuilder(await factory.StaffAsync(Roles.Admin));
        await build.PublishedAsync();
        (await factory.ScalarAsync<long>(Pending)).ShouldBeGreaterThanOrEqualTo(3);
        factory.Rebuilds.Clear();

        await factory.DrainOutboxAsync();

        factory.Rebuilds.ShouldBe(["project.published"]);
        (await factory.ScalarAsync<long>(Pending)).ShouldBe(0);

        // A change in flight on another instance (claimed) is not absorbed, and the older changes defer to it.
        await build.CategoryAsync();
        await build.CategoryAsync();
        await factory.ExecuteSqlAsync("""
            UPDATE store.outbox_message SET claimed_until = now() + interval '1 hour'
             WHERE id = (SELECT id FROM store.outbox_message WHERE type = 'CatalogChanged' AND processed_at IS NULL ORDER BY occurred_at DESC, id DESC LIMIT 1)
            """);
        factory.Rebuilds.Clear();
        await factory.DrainOutboxAsync();
        factory.Rebuilds.ShouldBeEmpty();
        (await factory.ScalarAsync<long>(Pending)).ShouldBe(1);

        await factory.ExecuteSqlAsync("UPDATE store.outbox_message SET claimed_until = NULL, next_attempt_at = now() - interval '1 day' WHERE type = 'CatalogChanged' AND processed_at IS NULL");
        await factory.DrainOutboxAsync();
        factory.Rebuilds.ShouldBe(["category.created"]);
        (await factory.ScalarAsync<long>(Pending)).ShouldBe(0);
    }

    /// Every test starts with no pending catalogue change (the tests of this class share one database).
    private Task ResetAsync() =>
        factory.ExecuteSqlAsync("UPDATE store.outbox_message SET processed_at = now(), claimed_until = NULL WHERE processed_at IS NULL");

    private async Task<List<Guid>> PendingIdsAsync()
    {
        List<Guid> ids = [];
        await using var connection = new Npgsql.NpgsqlConnection(factory.DatabaseConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT id FROM store.outbox_message WHERE type = 'CatalogChanged' AND processed_at IS NULL ORDER BY occurred_at, id", connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    [Fact]
    public async Task A_newer_change_arriving_while_an_older_one_is_handled_still_rebuilds_once()
    {
        await ResetAsync();
        var build = new CatalogBuilder(await factory.StaffAsync(Roles.Admin));
        await build.CategoryAsync();
        await build.CategoryAsync();
        var ids = await PendingIdsAsync();
        ids.Count.ShouldBe(2);

        // The older row is being handled (claimed); the newer one arrived after the claim and is not claimed.
        await factory.ExecuteSqlAsync($"UPDATE store.outbox_message SET claimed_until = now() + interval '5 minutes' WHERE id = '{ids[0]}'");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var debounce = scope.ServiceProvider.GetRequiredService<ICatalogChangeDebounce>();
            (await debounce.AbsorbAsync(ids[0], factory.Clock.UtcNow, TestContext.Current.CancellationToken)).ShouldBeFalse();
        }

        (await PendingIdsAsync()).ShouldContain(ids[1]);
        await factory.ExecuteSqlAsync($"UPDATE store.outbox_message SET processed_at = now(), claimed_until = NULL WHERE id = '{ids[0]}'");
        factory.Rebuilds.Clear();
        await factory.DrainOutboxAsync();

        factory.Rebuilds.ShouldBe(["category.created"]);
    }

    [Fact]
    public async Task An_older_change_in_retry_backoff_is_absorbed_by_a_newer_due_one()
    {
        await ResetAsync();
        var build = new CatalogBuilder(await factory.StaffAsync(Roles.Admin));
        await build.CategoryAsync();
        var older = (await PendingIdsAsync()).Single();
        await factory.ExecuteSqlAsync($"UPDATE store.outbox_message SET attempts = 1, next_attempt_at = now() + interval '1 hour', last_error = 'boom' WHERE id = '{older}'");
        await build.CategoryAsync();
        factory.Rebuilds.Clear();

        await factory.DrainOutboxAsync();

        factory.Rebuilds.ShouldBe(["category.created"]);
        (await PendingIdsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_burst_of_five_changes_is_one_rebuild()
    {
        await ResetAsync();
        var build = new CatalogBuilder(await factory.StaffAsync(Roles.Admin));
        for (var i = 0; i < 5; i++)
        {
            await build.CategoryAsync();
        }

        (await PendingIdsAsync()).Count.ShouldBe(5);
        factory.Rebuilds.Clear();

        await factory.DrainOutboxAsync();

        factory.Rebuilds.Count.ShouldBe(1);
        (await PendingIdsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public void Production_refuses_to_start_without_the_rebuild_token()
    {
        using var production = new ProductionWithoutRebuildFactory();

        var failure = Should.Throw<Exception>(() => production.CreateClient());

        failure.ToString().ShouldContain("Store:SiteRebuild:Token");
    }
}
