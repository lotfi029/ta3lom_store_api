using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Catalog;
using Ta3lom.Store.Domain.Services;

namespace Ta3lom.Store.Application.Features.Catalog.Queries.GetGiftFeed;

/// `GET /v1/catalog/products/{slug}/feed` (#7, FR-S06, RUL-23): the last 10 paid gift lines. Anonymous orders
/// show "فاعل خير"; other givers show first name + initial (09 OP-18); sponsorships show units, not money.
public sealed record GetGiftFeedQuery(string Slug) : IQuery<IReadOnlyList<GiftFeedItem>>;

internal sealed class GetGiftFeedQueryHandler(CatalogReader reader, IApplicationDbContext db) : IQueryHandler<GetGiftFeedQuery, IReadOnlyList<GiftFeedItem>>
{
    private static readonly OrderStatus[] Paid = [OrderStatus.Paid, OrderStatus.Fulfilled, OrderStatus.PartiallyRefunded];

    public async Task<Result<IReadOnlyList<GiftFeedItem>>> HandleAsync(GetGiftFeedQuery query, CancellationToken ct = default)
    {
        var productId = await reader.Published.Where(p => p.Slug == query.Slug).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        if (productId is null)
        {
            return CatalogErrors.ProductNotFound;
        }

        var rows = await (from l in db.OrderLines.AsNoTracking()
                          join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
                          where l.ProductId == productId && l.Kind != LineKind.Buy && Paid.Contains(o.Status) && o.PaidAt != null
                          orderby o.PaidAt descending
                          select new { l.Kind, l.Mode, l.Quantity, l.Amount, o.IsAnonymous, o.ContactName, PaidAt = o.PaidAt!.Value })
            .Take(10).ToListAsync(ct);

        return rows.Select(r =>
        {
            var (ar, en) = DonorDisplay.For(r.IsAnonymous, r.ContactName);
            return r.Kind == LineKind.Sponsor
                ? new GiftFeedItem(ar, en, null, r.Mode == LineMode.Units ? r.Quantity : null, r.PaidAt)
                : new GiftFeedItem(ar, en, r.Amount.ToWire(), r.Mode == LineMode.Units ? r.Quantity : null, r.PaidAt);
        }).ToList();
    }
}
