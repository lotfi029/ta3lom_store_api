using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Infrastructure.Services.Jobs;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Api.IntegrationTests.Operations;

/// L-4: with several instances every Quartz scheduler fires each job; one scheduled fire time runs once across them.
/// A manual run (no scheduled fire time) always runs.
public sealed class JobRunTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private async Task<bool> RunAsync(DateTimeOffset? scheduledFire, Func<Result<int>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var ran = false;
        await scope.ServiceProvider.GetRequiredService<JobRuns>().RunAsync("ProbeJob", scheduledFire, _ =>
        {
            ran = true;
            return Task.FromResult(work());
        }, TestContext.Current.CancellationToken);
        return ran;
    }

    [Fact]
    public async Task One_scheduled_fire_runs_once_across_instances()
    {
        var fire = new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero);

        var runs = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => RunAsync(fire, () => 1)));

        runs.Count(r => r).ShouldBe(1);
        (await RunAsync(fire.AddDays(1), () => 1)).ShouldBeTrue();
        (await RunAsync(null, () => 1)).ShouldBeTrue();
        (await RunAsync(null, () => 1)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_failed_run_does_not_throw()
    {
        (await RunAsync(null, () => Error.Conflict("probe.failed", "probe"))).ShouldBeTrue();
    }
}
