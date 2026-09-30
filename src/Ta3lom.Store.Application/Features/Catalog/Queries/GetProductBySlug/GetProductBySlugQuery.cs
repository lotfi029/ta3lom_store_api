using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.DTOs.Catalog;

namespace Ta3lom.Store.Application.Features.Catalog.Queries.GetProductBySlug;

/// `GET /v1/catalog/products/{slug}` (#4, FR-S04–S06, RUL-02–RUL-10). Unknown or unpublished → 404.
public sealed record GetProductBySlugQuery(string Slug) : IQuery<ProductDetailResponse>;

internal sealed class GetProductBySlugQueryHandler(CatalogReader reader) : IQueryHandler<GetProductBySlugQuery, ProductDetailResponse>
{
    public async Task<Result<ProductDetailResponse>> HandleAsync(GetProductBySlugQuery query, CancellationToken ct = default)
    {
        var product = await reader.Published.FirstOrDefaultAsync(p => p.Slug == query.Slug, ct);
        return product is null ? CatalogErrors.ProductNotFound : await reader.DetailAsync(product, ct);
    }
}
