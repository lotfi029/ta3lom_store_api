namespace Ta3lom.Store.Application.Interfaces;

public enum ActorKind
{
    Anonymous,
    Customer,
    Staff,
    System,
}

/// Who is acting; feeds audit_log.actor (09 §3.4). Claims-based from S-04/S-05.
public interface ICurrentActor
{
    ActorKind Kind { get; }

    Guid? Id { get; }

    /// Staff email or a system job name; null for customers and anonymous callers.
    string? Name { get; }

    /// HMAC of the client IP (audit_log.ip_hash); null outside HTTP requests or until the key exists (S-04).
    string? IpHash { get; }
}
