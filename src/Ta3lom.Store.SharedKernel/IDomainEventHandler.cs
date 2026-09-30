namespace Ta3lom.Store.SharedKernel;

/// Handles a domain event (DMS). Store events become outbox rows in the same transaction (09 §3.4),
/// so handlers run from the outbox dispatcher, never inside the unit of work.
public interface IDomainEventHandler<in T>
    where T : IDomainEvent
{
    Task HandleAsync(T domainEvent, CancellationToken ct = default);
}
