namespace Ta3lom.Store.Application.Features.Cart.Commands.DeleteExpiredGuestCarts;

/// `ExpireGuestCarts` job (03:00 Riyadh, RUL-16, database/03 §5): deletes guest carts past `expires_at`.
public sealed record DeleteExpiredGuestCartsCommand : ICommand<int>;

internal sealed class DeleteExpiredGuestCartsCommandHandler(ICartRepository carts, IClock clock) : ICommandHandler<DeleteExpiredGuestCartsCommand, int>
{
    public async Task<Result<int>> HandleAsync(DeleteExpiredGuestCartsCommand command, CancellationToken ct = default) =>
        await carts.DeleteExpiredGuestCartsAsync(clock.UtcNow, ct);
}
