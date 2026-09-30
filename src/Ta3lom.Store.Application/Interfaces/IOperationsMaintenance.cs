namespace Ta3lom.Store.Application.Interfaces;

/// Bulk retention and pruning (database/03 §5, §7) — set-based SQL, so it lives in Infrastructure.
public interface IOperationsMaintenance
{
    /// `payment_event.payload` → `{}` for rows received before `before` (18 months). Returns rows changed.
    Task<int> ScrubPaymentPayloadsAsync(DateTimeOffset before, CancellationToken ct = default);

    /// `notification` rows created before `before` (12 months).
    Task<int> DeleteNotificationsAsync(DateTimeOffset before, CancellationToken ct = default);

    /// Processed `outbox_message` rows before `before` (30 days); `SponsorDigest` rows, the sponsors' "last digest" record,
    /// before `digestsBefore` (13 months). Notifications keep their row, not the link.
    Task<int> DeleteProcessedOutboxAsync(DateTimeOffset before, DateTimeOffset digestsBefore, CancellationToken ct = default);

    /// `download_log` rows (hashed IPs) before `before` (12 months).
    Task<int> DeleteDownloadLogAsync(DateTimeOffset before, CancellationToken ct = default);

    /// `idempotency_key` rows created before `before` (24 h, 11 §1.5).
    Task<int> PruneIdempotencyKeysAsync(DateTimeOffset before, CancellationToken ct = default);
}

/// ta3lom-web rebuild after a catalogue change (GitHub `repository_dispatch` when configured; otherwise a logged no-op).
public interface ISiteRebuildTrigger
{
    Task<bool> TriggerAsync(string reason, CancellationToken ct = default);
}
