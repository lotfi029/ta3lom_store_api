namespace Ta3lom.Store.Application.Features.Cart.Commands.ClearCart;

/// `DELETE /v1/cart` (#15): empties the cart (204 even without one).
public sealed record ClearCartCommand(Guid? GuestCartId) : ICommand;

internal sealed class ClearCartCommandHandler(CartAccess access, IUnitOfWork uow, IClock clock) : ICommandHandler<ClearCartCommand>
{
    public Task<Result> HandleAsync(ClearCartCommand command, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            var cart = await access.ExistingForWriteAsync(command.GuestCartId, token);
            cart?.Clear(clock.UtcNow);
            return Result.Success();
        }, ct);
}
