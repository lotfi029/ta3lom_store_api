namespace Ta3lom.Store.Domain.IRepositories;

/// One unit of work per command. No EF types here: transactions run inside the
/// provider's retrying execution strategy, so the whole body is retried on a serialization failure
/// and must not call external services.
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// Runs <paramref name="work"/> in one database transaction and commits it (saving changes first).
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct = default);

    /// Runs <paramref name="work"/> and returns its result; a failed <see cref="Result"/> rolls the transaction back unless
    /// <paramref name="commitOnFailure"/> is set (a wrong OTP must still persist `attempts + 1`;
    /// refresh-token reuse must still persist the family revocation).
    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> work, bool commitOnFailure, CancellationToken ct = default)
        where TResult : Result;

    /// Same, rolling back on a failed result.
    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CancellationToken ct = default)
        where TResult : Result;
}
