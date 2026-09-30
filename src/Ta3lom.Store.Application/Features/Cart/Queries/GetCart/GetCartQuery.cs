using Ta3lom.Store.Application.DTOs.Cart;

namespace Ta3lom.Store.Application.Features.Cart.Queries.GetCart;

/// `GET /v1/cart` (#10): the current cart, or an empty cart (`id: null`) without creating a row.
public sealed record GetCartQuery(Guid? GuestCartId) : IQuery<CartResponse>;

internal sealed class GetCartQueryHandler(CartAccess access, CartCatalog catalog) : IQueryHandler<GetCartQuery, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(GetCartQuery query, CancellationToken ct = default) =>
        await catalog.RenderAsync(await access.ForReadAsync(query.GuestCartId, ct), access.CustomerId, [], ct);
}
