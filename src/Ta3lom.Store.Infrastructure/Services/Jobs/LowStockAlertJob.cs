using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Fulfilment.Commands.LowStockAlert;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// Daily 08:00 Riyadh (`0 0 8 * * ?`, 09 §6): low print stock → staff e-mail, once per format per day (FR-S43).
[DisallowConcurrentExecution]
internal sealed class LowStockAlertJob(ICommandHandler<LowStockAlertCommand, int> handler) : IJob
{
    public const string Cron = "0 0 8 * * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new LowStockAlertCommand(), cancellationToken);
}
