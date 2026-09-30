using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.AdminOps.Commands;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// `0 0 4 * * ?` (09 §6) — 04:00 daily: the db/reference/003 queries; any row → staff alert.
[DisallowConcurrentExecution]
internal sealed class IntegrityChecksJob(ICommandHandler<IntegrityChecksCommand, int> handler) : IJob
{
    public const string Cron = "0 0 4 * * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new IntegrityChecksCommand(), cancellationToken);
}
