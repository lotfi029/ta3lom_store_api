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
        await factory.DrainOutboxAsync();
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

    [Fact]
    public void Production_refuses_to_start_without_the_rebuild_token()
    {
        using var production = new ProductionWithoutRebuildFactory();

        var failure = Should.Throw<Exception>(() => production.CreateClient());

        failure.ToString().ShouldContain("Store:SiteRebuild:Token");
    }
}
