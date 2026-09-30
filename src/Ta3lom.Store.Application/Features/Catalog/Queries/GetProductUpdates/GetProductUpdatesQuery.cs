using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Catalog;

namespace Ta3lom.Store.Application.Features.Catalog.Queries.GetProductUpdates;

/// `GET /v1/catalog/products/{slug}/updates` (#6, FR-S59, RUL-54): public delivery updates, newest first.
public sealed record GetProductUpdatesQuery(string Slug, int Page = 1, int PageSize = 20) : IQuery<PagedResult<CauseUpdateResponse>>;

internal sealed class GetProductUpdatesQueryHandler(CatalogReader reader, IApplicationDbContext db)
    : IQueryHandler<GetProductUpdatesQuery, PagedResult<CauseUpdateResponse>>
{
    public async Task<Result<PagedResult<CauseUpdateResponse>>> HandleAsync(GetProductUpdatesQuery query, CancellationToken ct = default)
    {
        var productId = await reader.Published.Where(p => p.Slug == query.Slug).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        if (productId is null)
        {
            return CatalogErrors.ProductNotFound;
        }

        var page = new PageRequest(query.Page, query.PageSize);
        var updates = db.CauseUpdates.AsNoTracking().Where(u => u.ProductId == productId && u.IsPublic);
        var total = await updates.CountAsync(ct);
        var rows = await updates.OrderByDescending(u => u.OccurredOn).ThenByDescending(u => u.CreatedAt).Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        var assetIds = rows.Where(r => r.AssetId != null).Select(r => r.AssetId!.Value).ToList();
        var assets = await db.MediaAssets.AsNoTracking().Where(m => assetIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        return new PagedResult<CauseUpdateResponse>(
            [.. rows.Select(u => new CauseUpdateResponse(u.Id, u.TitleAr, u.TitleEn, u.BodyAr, u.BodyEn,
                u.AssetId is { } a && assets.TryGetValue(a, out var m) ? reader.Image(m) : null, u.UnitsDelivered, u.OccurredOn))],
            total, page.SafePage, page.Take);
    }
}
