using Quartz;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// Quartz registration: RAM store, `DisallowConcurrentExecution` on every job, cron in Asia/Riyadh.
/// Jobs are thin shells: each resolves one Application command and runs it (Run-plan-review §2 #3).
public static class JobScheduling
{
    public static TimeZoneInfo Riyadh { get; } = FindRiyadh();

    /// Schedules `TJob` and records it in `jobs` (this host's list: name → cron).
    public static void AddStoreJob<TJob>(this IQuartzBuilder quartz, ScheduledJobs jobs, string cron)
        where TJob : class, IJob
    {
        ArgumentNullException.ThrowIfNull(quartz);
        ArgumentNullException.ThrowIfNull(jobs);
        var name = typeof(TJob).Name;
        jobs.Add(name, cron);
        quartz.ScheduleJob<TJob>(
            trigger => trigger.WithIdentity(name + ".trigger", "store")
                .WithCronSchedule(cron, c => c.InTimeZone(Riyadh)),
            job => job.WithIdentity(name, "store"));
    }

    private static TimeZoneInfo FindRiyadh()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Riyadh", TimeSpan.FromHours(3), "Asia/Riyadh", "AST");
        }
    }
}

/// The jobs this host scheduled (name → cron), for `/dev/jobs` and the job runner. Per host, not static: showcase mode
/// schedules fewer jobs than a live host.
public sealed class ScheduledJobs
{
    private readonly Dictionary<string, string> _crons = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Crons => _crons;

    internal void Add(string name, string cron) => _crons[name] = cron;
}
