using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.AdminOps.Commands;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// `0 30 4 ? * FRI` (09 §6) — Fridays 04:30: database/03 §7 retention (payload scrub, notification/outbox/download_log deletes, beneficiary PII).
[DisallowConcurrentExecution]
internal sealed class RetentionJob(ICommandHandler<RetentionCommand, int> handler) : IJob
{
    public const string Cron = "0 30 4 ? * FRI";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new RetentionCommand(), cancellationToken);
}
