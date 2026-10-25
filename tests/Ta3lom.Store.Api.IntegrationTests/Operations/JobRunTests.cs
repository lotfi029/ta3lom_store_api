using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Infrastructure.Persistence;
using Ta3lom.Store.Infrastructure.Services.Jobs;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Api.IntegrationTests.Operations;

/// L-4: with several instances every Quartz scheduler fires each job; one scheduled fire time runs once across them.
/// A manual run (no scheduled fire time) always runs.
public sealed class JobRunTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private async Task<bool> RunAsync(DateTimeOffset? scheduledFire, Func<Result<int>> work, ILogger<JobRuns>? logger = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var runs = logger is null
            ? sp.GetRequiredService<JobRuns>()
            : new JobRuns(sp.GetRequiredService<StoreDbContext>(), sp.GetRequiredService<IClock>(), logger);
        var ran = false;
        await runs.RunAsync("ProbeJob", scheduledFire, _ =>
        {
            ran = true;
            return Task.FromResult(work());
        }, TestContext.Current.CancellationToken);
        return ran;
    }

    /// R12-1: the fire time follows the clock (as Quartz's does), so the 30-day prune of `job_run` never removes it
    /// while the runners race — also when the test runs long after it was written.
    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    public async Task One_scheduled_fire_runs_once_across_instances(int daysAhead)
    {
        var real = factory.Clock.UtcNow;
        factory.Clock.Set(real.AddDays(daysAhead));
        try
        {
            var now = factory.Clock.UtcNow;
            var fire = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);

            var runs = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => RunAsync(fire, () => 1)));

            runs.Count(r => r).ShouldBe(1);
            (await RunAsync(fire.AddDays(1), () => 1)).ShouldBeTrue();
            (await RunAsync(null, () => 1)).ShouldBeTrue();
            (await RunAsync(null, () => 1)).ShouldBeTrue();
        }
        finally
        {
            factory.Clock.Set(real);
        }
    }

    [Fact]
    public async Task A_failed_run_does_not_throw_and_is_logged()
    {
        var logger = new CapturingLogger();

        (await RunAsync(null, () => Error.Conflict("probe.failed", "probe"), logger)).ShouldBeTrue();

        logger.Entries.ShouldContain((LogLevel.Error, "Job ProbeJob failed with probe.failed"));
    }

    private sealed class CapturingLogger : ILogger<JobRuns>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
