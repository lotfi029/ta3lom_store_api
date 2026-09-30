using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Giving.Commands;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// 00:05 daily (`0 5 0 * * ?`, 09 §6): offers past `ends_at` → inactive, pool remainder swept to the general fund (RUL-09, RUL-46).
[DisallowConcurrentExecution]
internal sealed class CloseCampaignsJob(ICommandHandler<CloseCampaignsCommand, int> handler) : IJob
{
    public const string Cron = "0 5 0 * * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new CloseCampaignsCommand(), cancellationToken);
}
