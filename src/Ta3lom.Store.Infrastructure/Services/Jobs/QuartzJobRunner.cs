using Quartz;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

internal sealed class QuartzJobRunner(ISchedulerFactory schedulers) : IJobRunner
{
    public IReadOnlyDictionary<string, string> Jobs => JobScheduling.Registered;

    public async Task<bool> TriggerAsync(string jobName, CancellationToken ct = default)
    {
        if (!JobScheduling.Registered.ContainsKey(jobName))
        {
            return false;
        }

        var scheduler = await schedulers.GetScheduler(ct);
        await scheduler.TriggerJob(new JobKey(jobName, "store"), null, ct);
        return true;
    }
}
