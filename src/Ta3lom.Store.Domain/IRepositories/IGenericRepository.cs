using System.Linq.Expressions;

namespace Ta3lom.Store.Domain.IRepositories;

/// DMS generic repository for commands (tracked aggregates). Reads for endpoints go through
/// IApplicationDbContext projections; row locks live in the specific Infrastructure repositories.
public interface IGenericRepository<T>
    where T : class
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task<IReadOnlyList<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    void Add(T entity);

    void AddRange(IEnumerable<T> entities);

    void Remove(T entity);
}
