using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Catalog;
using Ta3lom.Store.Application.Features.Catalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Queries.PreviewProduct;

/// `GET /v1/admin/products/{id}/preview` (#66, FR-S72): the public product page (§2.4) regardless of status.
public sealed record PreviewProductQuery(Guid Id) : IQuery<ProductDetailResponse>;

internal sealed class PreviewProductQueryHandler(IApplicationDbContext db, CatalogReader reader) : IQueryHandler<PreviewProductQuery, ProductDetailResponse>
{
    public async Task<Result<ProductDetailResponse>> HandleAsync(PreviewProductQuery query, CancellationToken ct = default)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == query.Id, ct);
        return product is null ? AdminCatalogErrors.ProductNotFound : await reader.DetailAsync(product, ct);
    }
}
