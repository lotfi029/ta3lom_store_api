using Ta3lom.Store.Application.DTOs.Cart;

namespace Ta3lom.Store.Application.Features.Cart.Commands.RemoveCartLine;

/// `DELETE /v1/cart/lines/{lineId}` (#14).
public sealed record RemoveCartLineCommand(Guid? GuestCartId, Guid LineId) : ICommand<CartResponse>;

internal sealed class RemoveCartLineCommandHandler(CartAccess access, CartCatalog catalog, IUnitOfWork uow, IClock clock)
    : ICommandHandler<RemoveCartLineCommand, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(RemoveCartLineCommand command, CancellationToken ct = default)
    {
        var changed = await uow.ExecuteInTransactionAsync(async token =>
        {
            var cart = await access.ExistingForWriteAsync(command.GuestCartId, token);
            if (cart is null)
            {
                return Result.Failure<Guid>(CartErrors.LineNotFound);
            }

            var result = cart.RemoveLine(command.LineId, clock.UtcNow);
            return result.IsFailure ? Result.Failure<Guid>(result.Error) : Result.Success(cart.Id);
        }, ct);
        return changed.IsFailure
            ? changed.Error
            : await catalog.RenderAsync(await access.ForReadAsync(changed.Value, ct), access.CustomerId, [], ct);
    }
}
