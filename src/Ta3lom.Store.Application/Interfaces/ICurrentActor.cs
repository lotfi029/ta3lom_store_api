namespace Ta3lom.Store.Application.Interfaces;

public enum ActorKind
{
    Anonymous,
    Customer,
    Staff,
    System,
}

/// Who is acting; feeds audit_log.actor (from the token claims).
public interface ICurrentActor
{
    ActorKind Kind { get; }

    Guid? Id { get; }

    /// Staff email or a system job name; null for customers and anonymous callers.
    string? Name { get; }

    /// HMAC of the client IP (audit_log.ip_hash); null outside HTTP requests or until the key exists.
    string? IpHash { get; }
}
