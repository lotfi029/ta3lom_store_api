using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Orders.Commands.ExpireUnpaidOrders;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// Every 5 minutes (`0 0/5 * * * ?`, 09 §6): unpaid orders past `payment_due_at` → `expired` (RUL-29, RUL-30).
[DisallowConcurrentExecution]
internal sealed class ExpireUnpaidOrdersJob(ICommandHandler<ExpireUnpaidOrdersCommand, int> handler) : IJob
{
    public const string Cron = "0 0/5 * * * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new ExpireUnpaidOrdersCommand(), cancellationToken);
}
