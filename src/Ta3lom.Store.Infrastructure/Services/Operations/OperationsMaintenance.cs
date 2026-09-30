using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Infrastructure.Services.Operations;

/// database/03 §5, §7 — set-based deletes and scrubs for the `Retention` and `PruneIdempotencyKeys` jobs.
internal sealed class OperationsMaintenance(StoreDbContext db) : IOperationsMaintenance
{
    public Task<int> ScrubPaymentPayloadsAsync(DateTimeOffset before, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"UPDATE store.payment_event SET payload = '{{}}'::jsonb WHERE received_at < {before} AND payload <> '{{}}'::jsonb", ct);

    public Task<int> DeleteNotificationsAsync(DateTimeOffset before, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"DELETE FROM store.notification WHERE created_at < {before}", ct);

    public async Task<int> DeleteProcessedOutboxAsync(DateTimeOffset before, DateTimeOffset digestsBefore, CancellationToken ct = default)
    {
        // Notifications (kept 12 months) point at their message for de-duplication; long after processing the link is
        // no longer needed, so it is cleared before the message is deleted (FK without ON DELETE).
        await db.Database.ExecuteSqlAsync($"""
            UPDATE store.notification SET outbox_message_id = NULL
             WHERE outbox_message_id IN (SELECT id FROM store.outbox_message
                                          WHERE processed_at < {before} AND (type <> 'SponsorDigest' OR processed_at < {digestsBefore}))
            """, ct);
        return await db.Database.ExecuteSqlAsync($"""
            DELETE FROM store.outbox_message
             WHERE processed_at < {before} AND (type <> 'SponsorDigest' OR processed_at < {digestsBefore})
            """, ct);
    }

    public Task<int> DeleteDownloadLogAsync(DateTimeOffset before, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"DELETE FROM store.download_log WHERE at < {before}", ct);

    public Task<int> PruneIdempotencyKeysAsync(DateTimeOffset before, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"DELETE FROM store.idempotency_key WHERE created_at < {before}", ct);
}
