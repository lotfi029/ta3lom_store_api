using Quartz;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Cart.Commands.DeleteExpiredGuestCarts;

namespace Ta3lom.Store.Infrastructure.Services.Jobs;

/// Daily 03:00 Riyadh (`0 0 3 * * ?`, 09 §7): deletes guest carts past `expires_at` (RUL-16, database/03 §5).
[DisallowConcurrentExecution]
internal sealed class ExpireGuestCartsJob(ICommandHandler<DeleteExpiredGuestCartsCommand, int> handler) : IJob
{
    public const string Cron = "0 0 3 * * ?";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new DeleteExpiredGuestCartsCommand(), cancellationToken);
}
