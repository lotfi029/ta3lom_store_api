using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Infrastructure.Services.Idempotency;

/// 09 §5.16 on the `idempotency_key` table. Each statement runs on its own (outside the request's unit of work).
internal sealed class IdempotencyStore(StoreDbContext db, IClock clock) : IIdempotencyStore
{
    public async Task<IdempotencyClaim> ClaimAsync(string scope, string key, string requestHash, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var claimed = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO store.idempotency_key (scope, key, request_hash, created_at) VALUES ({scope}, {key}, {requestHash}, {now})
            ON CONFLICT (scope, key) DO NOTHING
            """, ct);
        if (claimed == 1)
        {
            return new IdempotencyClaim(IdempotencyClaimStatus.Claimed);
        }

        var row = await db.Set<Persistence.Records.IdempotencyRecord>().AsNoTracking().SingleAsync(r => r.Scope == scope && r.Key == key, ct);
        if (row.RequestHash != requestHash)
        {
            return new IdempotencyClaim(IdempotencyClaimStatus.Mismatch);
        }

        return row.StatusCode is null
            ? new IdempotencyClaim(IdempotencyClaimStatus.InProgress)
            : new IdempotencyClaim(IdempotencyClaimStatus.Replay, row.StatusCode, row.Response);
    }

    public Task CompleteAsync(string scope, string key, int statusCode, string responseJson, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"""
            UPDATE store.idempotency_key SET status_code = {statusCode}, response = {responseJson}::jsonb
             WHERE scope = {scope} AND key = {key}
            """, ct);

    public Task ReleaseAsync(string scope, string key, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"DELETE FROM store.idempotency_key WHERE scope = {scope} AND key = {key}", ct);
}
