using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Domain.IRepositories;

namespace Ta3lom.Store.Infrastructure.Persistence.Repositories;

/// DMS generic repository over tracked entities. Specific repositories derive from it and add the
/// row-lock queries (`FOR UPDATE`, `SKIP LOCKED`, conditional `UPDATE`) that must stay in Infrastructure.
internal class GenericRepository<T>(StoreDbContext db) : IGenericRepository<T>
    where T : class
{
    protected StoreDbContext Db { get; } = db;

    protected DbSet<T> Set => Db.Set<T>();

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) => await Set.FindAsync([id], ct);

    public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(predicate, ct);

    public async Task<IReadOnlyList<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        await Set.Where(predicate).ToListAsync(ct);

    public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Set.AnyAsync(predicate, ct);

    public void Add(T entity) => Set.Add(entity);

    public void AddRange(IEnumerable<T> entities) => Set.AddRange(entities);

    public void Remove(T entity) => Set.Remove(entity);
}
