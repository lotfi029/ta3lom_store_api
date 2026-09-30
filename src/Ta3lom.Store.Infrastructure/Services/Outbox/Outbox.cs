using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Persistence;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Infrastructure.Services.Outbox;

internal sealed class Outbox(StoreDbContext db, IClock clock) : IOutbox
{
    public void Enqueue(IDomainEvent message) => db.OutboxMessages.Add(OutboxSerializer.ToMessage(message, clock.UtcNow));
}
