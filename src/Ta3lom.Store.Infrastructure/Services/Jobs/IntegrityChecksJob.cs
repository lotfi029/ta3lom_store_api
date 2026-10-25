using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Operations;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// `0 0 4 * * ?` — 04:00 daily: the integrity checks; any row → staff alert.
[DisallowConcurrentExecution]
internal sealed class IntegrityChecksJob(ICommandHandler<IntegrityChecksCommand, int> handler, JobRuns runs) : IJob
{
    public const string Cron = "0 0 4 * * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await runs.RunAsync(context.JobDetail.Key.Name, context.ScheduledFireTimeUtc, ct => handler.HandleAsync(new IntegrityChecksCommand(), ct), cancellationToken);
}
