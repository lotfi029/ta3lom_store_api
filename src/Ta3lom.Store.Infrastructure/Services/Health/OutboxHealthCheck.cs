using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Infrastructure.Services.Health;

/// L-3: the outbox is Degraded (readiness still 200) when a message was dead-lettered in the last 24 hours or a due message
/// has waited more than 15 minutes (the dispatcher is stuck or down). PostgreSQL being down is the postgres check's job.
internal sealed class OutboxHealthCheck(StoreDbContext db, IClock clock) : IHealthCheck
{
    public const string Name = "outbox";

    public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(15);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var since = now - TimeSpan.FromHours(24);
        var overdue = now - MaxWait;
        var row = (await db.Database.SqlQuery<Row>($"""
            SELECT count(*) FILTER (WHERE dead_lettered AND occurred_at >= {since}) AS dead_lettered,
                   count(*) FILTER (WHERE NOT dead_lettered AND processed_at IS NULL AND next_attempt_at <= {overdue}) AS overdue
              FROM store.outbox_message
             WHERE processed_at IS NULL
            """).ToListAsync(cancellationToken))[0];
        var data = new Dictionary<string, object> { ["deadLettered24h"] = row.DeadLettered, ["overdue"] = row.Overdue };
        return row.DeadLettered > 0 || row.Overdue > 0
            ? HealthCheckResult.Degraded("Outbox has dead-lettered or overdue messages", data: data)
            : HealthCheckResult.Healthy(data: data);
    }

    internal sealed record Row(long DeadLettered, long Overdue);
}
