namespace Ta3lom.Store.Domain.Entities.Auditing;

/// Table `audit_log`: append-only record of staff changes and security events (BR-A13). Never updated or deleted.
public sealed class AuditLogEntry
{
    public AuditLogEntry(string actor, string action, string entity, DateTimeOffset now,
        string? entityId = null, string? diff = null, string? ipHash = null)
    {
        At = now;
        Actor = Guard.NotNull(actor);
        Action = Guard.NotNull(action);
        Entity = Guard.NotNull(entity);
        EntityId = entityId;
        Diff = diff;
        IpHash = ipHash;
    }

    private AuditLogEntry()
    {
    }

    public long Id { get; private set; }

    public DateTimeOffset At { get; private set; }

    public string Actor { get; private set; } = null!;

    public string Action { get; private set; } = null!;

    public string Entity { get; private set; } = null!;

    public string? EntityId { get; private set; }

    public string? Diff { get; private set; }

    public string? IpHash { get; private set; }
}
