namespace Ta3lom.Store.Domain.Events;

/// Any staff change to a category, a project or its options: the public cache is evicted and the static web site is
/// rebuilt (debounced) from the outbox.
public sealed record CatalogChanged(string Entity, Guid Id, string Change) : IDomainEvent;

/// A donor placed a donation order (`pending_payment`). Money travels as the wire string.
public sealed record OrderPlaced(Guid OrderId, string Reference, string TotalAmount, int LineCount) : IDomainEvent;

/// Staff cancelled a pending order.
public sealed record OrderCancelled(Guid OrderId, string Reference, string CancelledBy) : IDomainEvent;
