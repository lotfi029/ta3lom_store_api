namespace Ta3lom.Store.Domain.Entities.Notifications;

/// Table `notification`: one row per message sent (RUL-63). Never holds the message body (the OTP code
/// or a dedication text never lands here). `ux_notification_once` makes outbox sends at-most-once.
public sealed class Notification : Entity
{
    public Notification(MessageChannel channel, string recipient, string templateKey, string locale, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        Channel = channel;
        Recipient = Guard.NotNull(recipient);
        TemplateKey = Guard.NotNull(templateKey);
        Locale = Guard.NotNull(locale);
        Status = NotificationStatus.Queued;
        CreatedAt = now;
    }

    private Notification()
    {
    }

    public MessageChannel Channel { get; private set; }

    public string Recipient { get; private set; } = null!;

    public string TemplateKey { get; private set; } = null!;

    public string Locale { get; private set; } = null!;

    public NotificationStatus Status { get; private set; }

    public string? ProviderMsgId { get; private set; }

    public string? Error { get; private set; }

    public Guid? RelatedOrderId { get; private set; }

    public Guid? OutboxMessageId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    /// A send that was not produced by the outbox (the OTP SMS, RUL-63 exception).
    public static Notification Direct(MessageChannel channel, string recipient, string templateKey, string locale, Guid? relatedOrderId, DateTimeOffset now) =>
        new(channel, recipient, templateKey, locale, now) { RelatedOrderId = relatedOrderId };

    /// A send claimed by an outbox handler; the unique index rejects a second row for the same message.
    public static Notification ForOutbox(Guid outboxMessageId, MessageChannel channel, string recipient, string templateKey, string locale, Guid? relatedOrderId, DateTimeOffset now) =>
        new(channel, recipient, templateKey, locale, now) { RelatedOrderId = relatedOrderId, OutboxMessageId = outboxMessageId };

    public void MarkSent(string? providerMessageId, DateTimeOffset now)
    {
        Status = NotificationStatus.Sent;
        ProviderMsgId = providerMessageId;
        Error = null;
        SentAt = now;
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        _ = now;
        Status = NotificationStatus.Failed;
        Error = error is { Length: > 500 } ? error[..500] : error;
    }
}
