namespace Ta3lom.Store.SharedKernel;

/// Raised by aggregates; converted into outbox rows in the same transaction (09 §3.4).
public interface IDomainEvent;
