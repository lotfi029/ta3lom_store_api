using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Domain.IRepositories;

public interface IProductRepository : IGenericRepository<Product>
{
    Task<bool> SlugTakenAsync(string slug, Guid? exceptId, CancellationToken ct = default);

    /// RUL-01: an order line exists for the product.
    Task<bool> HasOrdersAsync(Guid productId, CancellationToken ct = default);
}

public interface ICategoryRepository : IGenericRepository<Category>
{
    Task<bool> SlugTakenAsync(string slug, Guid? exceptId, CancellationToken ct = default);
}
