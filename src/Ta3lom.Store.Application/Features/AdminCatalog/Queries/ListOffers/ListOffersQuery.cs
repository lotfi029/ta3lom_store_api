using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.AdminCatalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Queries.ListOffers;

/// `GET /v1/admin/products/{id}/offers` (#68): offer history, newest first.
public sealed record ListOffersQuery(Guid ProductId) : IQuery<IReadOnlyList<OfferResponse>>;

internal sealed class ListOffersQueryHandler(IApplicationDbContext db, AdminCatalogReader reader) : IQueryHandler<ListOffersQuery, IReadOnlyList<OfferResponse>>
{
    public async Task<Result<IReadOnlyList<OfferResponse>>> HandleAsync(ListOffersQuery query, CancellationToken ct = default)
    {
        if (!await db.Products.AnyAsync(p => p.Id == query.ProductId, ct))
        {
            return AdminCatalogErrors.ProductNotFound;
        }

        var offers = await db.SponsorshipOffers.AsNoTracking().Where(o => o.ProductId == query.ProductId).OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
        List<OfferResponse> result = [];
        foreach (var offer in offers)
        {
            result.Add(await reader.OfferAsync(offer, ct));
        }

        return result;
    }
}
