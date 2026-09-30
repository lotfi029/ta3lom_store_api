using Ta3lom.Store.Application.DTOs.AdminCatalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Queries.GetAdminProduct;

/// `GET /v1/admin/products/{id}` (#56): all blocks, any status.
public sealed record GetAdminProductQuery(Guid Id) : IQuery<AdminProductResponse>;

internal sealed class GetAdminProductQueryHandler(AdminCatalogReader reader) : IQueryHandler<GetAdminProductQuery, AdminProductResponse>
{
    public async Task<Result<AdminProductResponse>> HandleAsync(GetAdminProductQuery query, CancellationToken ct = default) =>
        await reader.ProductAsync(query.Id, ct) is { } product ? product : AdminCatalogErrors.ProductNotFound;
}
