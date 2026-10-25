namespace Ta3lom.Store.SharedKernel;

/// Raised by aggregates; converted into outbox rows in the same transaction.
public interface IDomainEvent;
