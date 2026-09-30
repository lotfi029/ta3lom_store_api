using Ta3lom.Store.Domain.Entities.Notifications;

namespace Ta3lom.Store.Domain.IRepositories;

public interface INotificationRepository : IGenericRepository<Notification>
{
    /// 09 §5.11: once per (outbox message, channel, recipient) via `ux_notification_once`. Returns the row to send, or null
    /// when it was already sent or is in flight (`queued`). A `failed` row is re-queued and returned, so retries deliver.
    Task<Notification?> TryClaimAsync(Guid outboxMessageId, MessageChannel channel, string recipient, string templateKey, string locale,
        Guid? relatedOrderId, DateTimeOffset now, CancellationToken ct = default);
}
