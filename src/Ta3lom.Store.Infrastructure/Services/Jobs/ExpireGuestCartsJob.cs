using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Operations;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// Daily 03:00 Riyadh (`0 0 3 * * ?`): deletes guest carts idle for 30 days (D-14).
[DisallowConcurrentExecution]
internal sealed class ExpireGuestCartsJob(ICommandHandler<ExpireGuestCartsCommand, int> handler, JobRuns runs) : IJob
{
    public const string Cron = "0 0 3 * * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await runs.RunAsync(context.JobDetail.Key.Name, context.ScheduledFireTimeUtc, ct => handler.HandleAsync(new ExpireGuestCartsCommand(), ct), cancellationToken);
}
