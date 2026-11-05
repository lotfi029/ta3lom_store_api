using Quartz;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

internal sealed class QuartzJobRunner(ISchedulerFactory schedulers, ScheduledJobs scheduled) : IJobRunner
{
    public IReadOnlyDictionary<string, string> Jobs => scheduled.Crons;

    public async Task<bool> TriggerAsync(string jobName, CancellationToken ct = default)
    {
        if (!scheduled.Crons.ContainsKey(jobName))
        {
            return false;
        }

        var scheduler = await schedulers.GetScheduler(ct);
        await scheduler.TriggerJob(new JobKey(jobName, "store"), null, ct);
        return true;
    }
}
