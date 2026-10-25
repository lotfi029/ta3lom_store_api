using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Infrastructure.Persistence;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// L-4: runs one job's command once per scheduled fire time across instances (`job_run` claim) and logs a failed
/// `Result` (it was dropped before). A manual trigger has no scheduled fire time and always runs.
internal sealed partial class JobRuns(StoreDbContext db, IClock clock, ILogger<JobRuns> logger)
{
    private static readonly TimeSpan Keep = TimeSpan.FromDays(30);

    public async Task RunAsync<TResult>(string job, DateTimeOffset? scheduledFire, Func<CancellationToken, Task<Result<TResult>>> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (scheduledFire is { } fire)
        {
            var now = clock.UtcNow;
            var old = now - Keep;
            await db.Database.ExecuteSqlAsync($"DELETE FROM store.job_run WHERE fire_time < {old}", ct);
            var claimed = await db.Database.ExecuteSqlAsync($"""
                INSERT INTO store.job_run (job, fire_time, started_at) VALUES ({job}, {fire.ToUniversalTime()}, {now})
                ON CONFLICT (job, fire_time) DO NOTHING
                """, ct);
            if (claimed == 0)
            {
                LogSkipped(logger, job, fire);
                return;
            }
        }

        var result = await work(ct);
        if (result.IsFailure)
        {
            LogFailed(logger, job, result.Error.Code);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {Job} of {FireTime} already ran on another instance")]
    private static partial void LogSkipped(ILogger logger, string job, DateTimeOffset fireTime);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Job} failed with {Code}")]
    private static partial void LogFailed(ILogger logger, string job, string code);
}
