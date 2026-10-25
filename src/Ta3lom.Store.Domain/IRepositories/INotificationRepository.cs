using Ta3lom.Store.Domain.Entities.Notifications;

namespace Ta3lom.Store.Domain.IRepositories;

public interface INotificationRepository : IGenericRepository<Notification>
{
    /// At-most-once per `dedupe_key`: inserts the row as `failed` (not delivered yet) and returns it, re-claims a row that
    /// failed before, and returns null when the message was already sent.
    Task<Notification?> TryClaimAsync(string dedupeKey, MessageChannel channel, string recipientHash, string templateKey,
        DateTimeOffset now, CancellationToken ct = default);
}
