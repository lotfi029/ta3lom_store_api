using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Domain.Notifications;

/// Table `notification` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
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
}
