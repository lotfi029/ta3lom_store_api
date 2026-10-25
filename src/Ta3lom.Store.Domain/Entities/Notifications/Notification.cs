namespace Ta3lom.Store.Domain.Entities.Notifications;

/// Table `notification`: one row per message the store tried to send. Never holds the body (an OTP code never lands here)
/// nor the address in clear (`recipient_hash`). The unique `dedupe_key` makes outbox sends at-most-once: the row is claimed
/// as `failed` (not delivered yet) before the provider call and marked `sent` after it.
public sealed class Notification : Entity
{
    public Notification(MessageChannel channel, string recipientHash, string templateKey, string? dedupeKey, DateTimeOffset now)
        : base(Guid.CreateVersion7())
    {
        Channel = channel;
        RecipientHash = Guard.NotEmpty(recipientHash);
        TemplateKey = Guard.NotEmpty(templateKey);
        DedupeKey = dedupeKey;
        Status = NotificationStatus.Failed;
        CreatedAt = now;
    }

    private Notification()
    {
    }

    public MessageChannel Channel { get; private set; }

    public string RecipientHash { get; private set; } = null!;

    public string TemplateKey { get; private set; } = null!;

    public string? DedupeKey { get; private set; }

    public NotificationStatus Status { get; private set; }

    public string? ProviderRef { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkSent(string? providerRef)
    {
        Status = NotificationStatus.Sent;
        ProviderRef = providerRef is { Length: > 200 } ? providerRef[..200] : providerRef;
    }

    public void MarkFailed() => Status = NotificationStatus.Failed;
}
