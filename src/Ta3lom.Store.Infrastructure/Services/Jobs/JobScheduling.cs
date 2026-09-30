using Quartz;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// Quartz registration (09 §6): RAM store, `DisallowConcurrentExecution` on every job, cron in Asia/Riyadh.
/// Jobs are thin shells: each resolves one Application command and runs it (Run-plan-review §2 #3).
public static class JobScheduling
{
    public static TimeZoneInfo Riyadh { get; } = FindRiyadh();

    /// Every registered job: name → cron (used by the coverage audit and /dev tooling).
    public static IReadOnlyDictionary<string, string> Registered => Crons;

    private static readonly Dictionary<string, string> Crons = new(StringComparer.Ordinal);

    public static void AddStoreJob<TJob>(this IQuartzBuilder quartz, string cron)
        where TJob : class, IJob
    {
        ArgumentNullException.ThrowIfNull(quartz);
        var name = typeof(TJob).Name;
        lock (Crons)
        {
            Crons[name] = cron;
        }

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
