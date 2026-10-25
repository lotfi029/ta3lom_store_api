using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Operations;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// `0 30 * * * ?` — hourly at :30: idempotency keys past their 24-hour expiry (D-14).
[DisallowConcurrentExecution]
internal sealed class PruneIdempotencyKeysJob(ICommandHandler<PruneIdempotencyKeysCommand, int> handler) : IJob
{
    public const string Cron = "0 30 * * * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new PruneIdempotencyKeysCommand(), cancellationToken);
}
