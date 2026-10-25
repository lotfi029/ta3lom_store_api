using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Infrastructure.Services.Idempotency;

/// The `idempotency_key` table. Each statement runs on its own (outside the request's unit of work), so a claim is visible
/// to a parallel request at once. A row past `expires_at` no longer counts and is replaced, and so does an `in_progress`
/// claim older than <see cref="SessionPolicy.IdempotencyStaleClaim"/> (A-5: left by a crash; the use case itself recognises
/// work that did commit, e.g. checkout finds the order placed with the key).
internal sealed class IdempotencyStore(StoreDbContext db, IClock clock) : IIdempotencyStore
{
    /// L-2: when the insert loses to a row that is released before it can be read, the claim is tried once more; a row
    /// that is still gone after that reads as in progress (409), never as an error.
    public async Task<IdempotencyClaim> ClaimAsync(string scope, string key, string requestHash, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var now = clock.UtcNow;
            var expiresAt = now + SessionPolicy.IdempotencyRetention;
            var stale = now - SessionPolicy.IdempotencyStaleClaim;
            await db.Database.ExecuteSqlAsync($"""
                DELETE FROM store.idempotency_key
                 WHERE scope = {scope} AND key = {key} AND (expires_at <= {now} OR (status = 'in_progress' AND created_at <= {stale}))
                """, ct);
            var claimed = await db.Database.ExecuteSqlAsync($"""
                INSERT INTO store.idempotency_key (scope, key, request_hash, status, created_at, expires_at)
                VALUES ({scope}, {key}, {requestHash}, 'in_progress', {now}, {expiresAt})
                ON CONFLICT (scope, key) DO NOTHING
                """, ct);
            if (claimed == 1)
            {
                return new IdempotencyClaim(IdempotencyClaimStatus.Claimed);
            }

            var row = await db.Set<Persistence.Records.IdempotencyRecord>().AsNoTracking().SingleOrDefaultAsync(r => r.Scope == scope && r.Key == key, ct);
            if (row is null)
            {
                continue;
            }

            if (row.RequestHash != requestHash)
            {
                return new IdempotencyClaim(IdempotencyClaimStatus.Mismatch);
            }

            return row.Status == "completed"
                ? new IdempotencyClaim(IdempotencyClaimStatus.Replay, row.ResponseStatus, row.ResponseBody)
                : new IdempotencyClaim(IdempotencyClaimStatus.InProgress);
        }

        return new IdempotencyClaim(IdempotencyClaimStatus.InProgress);
    }

    public Task CompleteAsync(string scope, string key, int statusCode, string responseJson, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"""
            UPDATE store.idempotency_key SET status = 'completed', response_status = {statusCode}, response_body = {responseJson}::jsonb
             WHERE scope = {scope} AND key = {key}
            """, ct);

    public Task ReleaseAsync(string scope, string key, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"DELETE FROM store.idempotency_key WHERE scope = {scope} AND key = {key}", ct);
}
