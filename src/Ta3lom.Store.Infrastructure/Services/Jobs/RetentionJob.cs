using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Operations;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// `0 30 4 ? * FRI` — Fridays 04:30: retention (old notifications and processed outbox rows).
[DisallowConcurrentExecution]
internal sealed class RetentionJob(ICommandHandler<RetentionCommand, int> handler) : IJob
{
    public const string Cron = "0 30 4 ? * FRI";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new RetentionCommand(), cancellationToken);
}
