using Ta3lom.Store.Domain.Entities.Notifications;

namespace Ta3lom.Store.Domain.IRepositories;

public interface INotificationRepository : IGenericRepository<Notification>
{
    /// At-most-once per `dedupe_key`: inserts the row as `sending` (claimed), re-claims a row the provider refused before
    /// (`failed`), and otherwise returns the existing row unclaimed (`sent`, `unknown`, or `sending` = outcome unknown).
    Task<NotificationClaim> TryClaimAsync(string dedupeKey, MessageChannel channel, string recipientHash, string templateKey,
        DateTimeOffset now, CancellationToken ct = default);
}

/// The notification row (tracked) and whether this attempt owns the send.
public sealed record NotificationClaim(Notification Row, bool Claimed);
