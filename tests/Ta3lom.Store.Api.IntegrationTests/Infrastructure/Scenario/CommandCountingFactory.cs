using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;

/// A scenario host that counts the SQL commands EF Core sends, so a test can assert that a listing is a constant number
/// of round trips (no query per row). Count around one request at a time.
public sealed class CommandCountingFactory : ScenarioFactory
{
    public CommandCounter Commands { get; } = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.ConfigureDbContext<StoreDbContext>(o => o.AddInterceptors(Commands));
    }
}

public sealed class CommandCounter : DbCommandInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    /// The number of commands <paramref name="action"/> caused.
    public async Task<int> CountAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Reset();
        await action();
        return Count;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}
