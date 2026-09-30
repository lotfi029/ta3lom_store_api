using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Domain.IRepositories;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Infrastructure.Persistence.Repositories;

/// DMS unit of work with the store's (K-b) shape: the transaction runs inside Npgsql's retrying execution
/// strategy (database/01 §4.4). On a retry the change tracker is cleared, so the work must (re)load what it
/// changes inside the delegate and must not call external services.
internal sealed class UnitOfWork(StoreDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        await ExecuteInTransactionAsync(
            async token =>
            {
                await work(token);
                return Result.Success();
            },
            ct);
    }

    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CancellationToken ct = default)
        where TResult : Result => ExecuteInTransactionAsync(work, commitOnFailure: false, ct);

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> work, bool commitOnFailure, CancellationToken ct = default)
        where TResult : Result
    {
        ArgumentNullException.ThrowIfNull(work);
        if (db.Database.CurrentTransaction is not null)
        {
            // Already inside a unit of work: join it.
            return await work(ct);
        }

        var attempt = 0;
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            async token =>
            {
                if (attempt++ > 0)
                {
                    db.ChangeTracker.Clear();
                }

                await using var transaction = await db.Database.BeginTransactionAsync(token);
                var result = await work(token);
                if (result.IsFailure && !commitOnFailure)
                {
                    await transaction.RollbackAsync(token);
                    db.ChangeTracker.Clear();
                    return result;
                }

                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                return result;
            },
            ct);
    }
}
