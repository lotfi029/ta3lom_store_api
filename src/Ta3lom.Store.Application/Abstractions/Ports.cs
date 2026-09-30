namespace Ta3lom.Store.Application.Abstractions;

/// One SaveChanges per command; the DbContext implements it from S-02.
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}

/// The only source of time in src (09 §3.5). Business dates are Asia/Riyadh.
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    DateOnly RiyadhToday { get; }

    DateTimeOffset ToRiyadh(DateTimeOffset instant);
}

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

/// Typed read of the `setting` table (RUL §M). Implemented in S-02.
public interface ISettingsProvider
{
    Task<T> GetAsync<T>(string key, CancellationToken ct);
}
