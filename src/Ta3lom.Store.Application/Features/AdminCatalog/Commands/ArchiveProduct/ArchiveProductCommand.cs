using Ta3lom.Store.Application.DTOs.AdminCatalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.ArchiveProduct;

/// `POST /v1/admin/products/{id}/archive` (#65, RUL-02): leaves the catalogue; history keeps it.
public sealed record ArchiveProductCommand(Guid ProductId) : ICommand<AdminProductResponse>;

internal sealed class ArchiveProductCommandHandler(IProductRepository products, AdminCatalogReader reader, IUnitOfWork uow, IClock clock)
    : ICommandHandler<ArchiveProductCommand, AdminProductResponse>
{
    public async Task<Result<AdminProductResponse>> HandleAsync(ArchiveProductCommand command, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(command.ProductId, ct);
        if (product is null)
        {
            return AdminCatalogErrors.ProductNotFound;
        }

        product.Archive(clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return (await reader.ProductAsync(product.Id, ct))!;
    }
}
