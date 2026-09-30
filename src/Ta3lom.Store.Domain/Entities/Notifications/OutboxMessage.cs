namespace Ta3lom.Store.Domain.Entities.Notifications;

/// Table `outbox_message` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class OutboxMessage : Entity
{
    public OutboxMessage(string type, string payload, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        Type = Guard.NotNull(type);
        Payload = Guard.NotNull(payload);
        OccurredAt = now;
        Attempts = 0;
        NextAttemptAt = now;
    }

    private OutboxMessage()
    {
    }

    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }
}
