using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Infrastructure.Health;

/// `SELECT 1` with a 2 s budget (09 §8.3). Bypasses the retrying execution strategy so an outage is
/// reported at once; the exception text never reaches the response.
internal sealed class PostgresHealthCheck(StoreDbContext db) : IHealthCheck
{
    public const string Name = "postgres";

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Budget);
        try
        {
            await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
            await connection.OpenAsync(cts.Token);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cts.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is NpgsqlException or OperationCanceledException or TimeoutException or System.Net.Sockets.SocketException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL unreachable", ex);
        }
    }
}
