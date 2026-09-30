using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.IRepositories;

namespace Ta3lom.Store.Infrastructure.Persistence.Repositories;

internal sealed class ProductRepository(StoreDbContext db) : GenericRepository<Product>(db), IProductRepository
{
    public Task<bool> SlugTakenAsync(string slug, Guid? exceptId, CancellationToken ct = default) =>
        Set.AnyAsync(p => p.Slug == slug && p.Id != exceptId, ct);

    public Task<bool> HasOrdersAsync(Guid productId, CancellationToken ct = default) =>
        Db.OrderLines.AnyAsync(l => l.ProductId == productId, ct);
}

internal sealed class CategoryRepository(StoreDbContext db) : GenericRepository<Category>(db), ICategoryRepository
{
    public Task<bool> SlugTakenAsync(string slug, Guid? exceptId, CancellationToken ct = default) =>
        Set.AnyAsync(c => c.Slug == slug && c.Id != exceptId, ct);
}
