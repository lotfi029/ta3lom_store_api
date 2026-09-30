using Ta3lom.Store.Application.DTOs.Cart;

namespace Ta3lom.Store.Application.Features.Cart.Commands.UpdateCartLine;

/// `PATCH /v1/cart/lines/{lineId}` (#13): `units` lines only (RUL-12, RUL-18, RUL-30).
public sealed record UpdateCartLineCommand(Guid? GuestCartId, Guid LineId, int Quantity) : ICommand<CartResponse>;

internal sealed class UpdateCartLineCommandHandler(CartAccess access, CartCatalog catalog, IUnitOfWork uow, IClock clock)
    : ICommandHandler<UpdateCartLineCommand, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(UpdateCartLineCommand command, CancellationToken ct = default)
    {
        var changed = await uow.ExecuteInTransactionAsync(async token =>
        {
            var cart = await access.ExistingForWriteAsync(command.GuestCartId, token);
            var line = cart?.Lines.FirstOrDefault(l => l.Id == command.LineId);
            if (cart is null || line is null)
            {
                return Result.Failure<Guid>(CartErrors.LineNotFound);
            }

            var snapshots = await catalog.SnapshotsAsync([(line.ProductId, line.BookFormatId)], token);
            if (!snapshots.TryGetValue((line.ProductId, line.BookFormatId), out var product))
            {
                return Result.Failure<Guid>(CartErrors.ProductUnavailable);
            }

            var result = cart.UpdateQuantity(command.LineId, command.Quantity, product, await catalog.LimitsAsync(token), clock.UtcNow);
            return result.IsFailure ? Result.Failure<Guid>(result.Error) : Result.Success(cart.Id);
        }, ct);
        return changed.IsFailure
            ? changed.Error
            : await catalog.RenderAsync(await access.ForReadAsync(changed.Value, ct), access.CustomerId, [], ct);
    }
}
