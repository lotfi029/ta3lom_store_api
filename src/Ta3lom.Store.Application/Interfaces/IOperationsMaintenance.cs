namespace Ta3lom.Store.Application.Interfaces;

/// Bulk retention and pruning — set-based SQL, so it lives in Infrastructure.
public interface IOperationsMaintenance
{
    /// `notification` rows created before <paramref name="before"/> (12 months).
    Task<int> DeleteNotificationsAsync(DateTimeOffset before, CancellationToken ct = default);

    /// Processed `outbox_message` rows processed before <paramref name="before"/> (30 days).
    Task<int> DeleteProcessedOutboxAsync(DateTimeOffset before, CancellationToken ct = default);

    /// `idempotency_key` rows whose `expires_at` is before <paramref name="now"/>.
    Task<int> PruneIdempotencyKeysAsync(DateTimeOffset now, CancellationToken ct = default);

    /// `otp_challenge` rows created before <paramref name="before"/>.
    Task<int> PruneOtpChallengesAsync(DateTimeOffset before, CancellationToken ct = default);

    /// Guest carts (no customer) whose `expires_at` is before <paramref name="now"/>; their lines cascade.
    Task<int> DeleteExpiredGuestCartsAsync(DateTimeOffset now, CancellationToken ct = default);
}

/// Rebuild of the static web site after a catalogue change (GitHub `repository_dispatch` when configured; otherwise a logged no-op).
public interface ISiteRebuildTrigger
{
    Task<bool> TriggerAsync(string reason, CancellationToken ct = default);
}

/// Debounce of the site rebuild. Pending `CatalogChanged` rows that no dispatcher has claimed are absorbed (marked processed)
/// by the one being handled; a claimed row (in flight here or elsewhere) is never touched. Only the newest pending change
/// triggers: an older one is done when a newer one exists (pending or handled), since that one rebuilds after it.
public interface ICatalogChangeDebounce
{
    /// True when this message should trigger the rebuild.
    Task<bool> AbsorbAsync(Guid handledId, DateTimeOffset now, CancellationToken ct = default);
}
