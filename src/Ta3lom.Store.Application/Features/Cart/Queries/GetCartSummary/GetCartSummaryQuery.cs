using Ta3lom.Store.Application.DTOs.Cart;

namespace Ta3lom.Store.Application.Features.Cart.Queries.GetCartSummary;

/// `GET /v1/cart/summary` (#11, FR-S19): header badge — line count and grand total, no lines.
public sealed record GetCartSummaryQuery(Guid? GuestCartId) : IQuery<CartBadgeResponse>;

internal sealed class GetCartSummaryQueryHandler(CartAccess access, CartCatalog catalog) : IQueryHandler<GetCartSummaryQuery, CartBadgeResponse>
{
    public async Task<Result<CartBadgeResponse>> HandleAsync(GetCartSummaryQuery query, CancellationToken ct = default) =>
        await catalog.BadgeAsync(await access.ForReadAsync(query.GuestCartId, ct), ct);
}
