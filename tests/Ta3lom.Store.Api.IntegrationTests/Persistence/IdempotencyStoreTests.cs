using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Api.IntegrationTests.Persistence;

/// L-2: a claim that loses the insert race to a row released a moment later retries instead of throwing.
public sealed class IdempotencyStoreTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    [Fact]
    public async Task Claims_racing_releases_never_throw()
    {
        for (var round = 0; round < 40; round++)
        {
            var key = $"race-{round}-{Guid.NewGuid():N}";
            var tasks = Enumerable.Range(0, 6).Select(async i =>
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
                var claim = await store.ClaimAsync("race", key, new string('a', 64), TestContext.Current.CancellationToken);
                if (claim.Status == IdempotencyClaimStatus.Claimed && i % 2 == 0)
                {
                    await store.ReleaseAsync("race", key, TestContext.Current.CancellationToken);
                }

                return claim.Status;
            });

            (await Task.WhenAll(tasks)).ShouldAllBe(s => s == IdempotencyClaimStatus.Claimed || s == IdempotencyClaimStatus.InProgress);
        }
    }
}
