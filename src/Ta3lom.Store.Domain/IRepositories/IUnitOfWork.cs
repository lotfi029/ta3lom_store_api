namespace Ta3lom.Store.Domain.IRepositories;

/// One unit of work per command (09 §3.4). No EF types here (K-b): transactions run inside the
/// provider's retrying execution strategy, so the whole body is retried on a serialization failure
/// and must not call external services (database/01 §4.4).
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// Runs <paramref name="work"/> in one database transaction and commits it (saving changes first).
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct = default);

    /// Same, returning the body's result; a failed <see cref="Result"/> rolls the transaction back.
    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CancellationToken ct = default)
        where TResult : Result;
}
