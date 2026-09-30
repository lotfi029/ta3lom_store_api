namespace Ta3lom.Store.Application.Interfaces;

/// Adds an `outbox_message` row to the current unit of work (09 §3.4). Aggregates raise their events themselves;
/// this port is for messages that belong to no loaded aggregate (e.g. a dedication card, a finance alert after a rollback).
public interface IOutbox
{
    void Enqueue(IDomainEvent message);
}
