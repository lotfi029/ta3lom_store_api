using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Allocation.Commands;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// `0 0 7 * * ?` (09 §6) — 07:00 daily: units awaiting allocation past 30/90 days → staff alert / manager escalation, once per product per day (RUL-49).
[DisallowConcurrentExecution]
internal sealed class AllocationAlertsJob(ICommandHandler<RaiseAllocationAlertsCommand, int> handler) : IJob
{
    public const string Cron = "0 0 7 * * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new RaiseAllocationAlertsCommand(), cancellationToken);
}
