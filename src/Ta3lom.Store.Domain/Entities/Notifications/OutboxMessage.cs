namespace Ta3lom.Store.Domain.Entities.Notifications;

/// Table `outbox_message`: a domain event written in the same transaction as the change that raised it, delivered after
/// commit. The dispatcher claims rows with a per-row lease (`claimed_until`) and dead-letters after the maximum attempts.
public sealed class OutboxMessage : Entity
{
    public OutboxMessage(string type, string payload, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        Type = Guard.NotEmpty(type);
        Payload = Guard.NotEmpty(payload);
        OccurredAt = now;
        NextAttemptAt = now;
    }

    private OutboxMessage()
    {
    }

    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    /// Lease of the dispatcher that claimed the row; another instance may take it once it has passed.
    public DateTimeOffset? ClaimedUntil { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? LastError { get; private set; }

    public bool DeadLettered { get; private set; }
}
