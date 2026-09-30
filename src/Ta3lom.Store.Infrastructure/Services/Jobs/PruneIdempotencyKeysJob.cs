using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.AdminOps.Commands;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// `0 30 * * * ?` (09 §6) — hourly at :30: idempotency keys older than 24 h (11 §1.5).
[DisallowConcurrentExecution]
internal sealed class PruneIdempotencyKeysJob(ICommandHandler<PruneIdempotencyKeysCommand, int> handler) : IJob
{
    public const string Cron = "0 30 * * * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new PruneIdempotencyKeysCommand(), cancellationToken);
}
