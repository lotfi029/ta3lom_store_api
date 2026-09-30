using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Catalog;

namespace Ta3lom.Store.Application.Features.Catalog.Queries.ListProductSlugs;

/// `GET /v1/catalog/products?fields=slug` (#3): slim rows for the main site's prerender route list.
public sealed record ListProductSlugsQuery(string? Type, int Page = 1, int PageSize = 100) : IQuery<PagedResult<ProductSlugResponse>>;

internal sealed class ListProductSlugsQueryHandler(CatalogReader reader) : IQueryHandler<ListProductSlugsQuery, PagedResult<ProductSlugResponse>>
{
    public async Task<Result<PagedResult<ProductSlugResponse>>> HandleAsync(ListProductSlugsQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var products = reader.Published;
        if (query.Type is not null && EnumWire.TryParse<ProductType>(query.Type, out var type))
        {
            products = products.Where(p => p.Type == type);
        }

        var total = await products.CountAsync(ct);
        var rows = await products.OrderBy(p => p.Type).ThenBy(p => p.Slug).Skip(page.Skip).Take(page.Take)
            .Select(p => new { p.Type, p.Slug, p.UpdatedAt }).ToListAsync(ct);
        return new PagedResult<ProductSlugResponse>([.. rows.Select(r => new ProductSlugResponse(r.Type.ToWire(), r.Slug, r.UpdatedAt))], total, page.SafePage, page.Take);
    }
}
