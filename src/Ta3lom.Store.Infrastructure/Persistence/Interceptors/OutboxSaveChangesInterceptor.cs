using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Infrastructure.Services.Outbox;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Infrastructure.Persistence.Interceptors;

/// 09 §3.4: every domain event raised by a tracked aggregate becomes an `outbox_message` row in the same SaveChanges,
/// so the message commits (or rolls back) with the business change. Nothing is published in-process.
internal sealed class OutboxSaveChangesInterceptor(IClock clock, Services.Outbox.OutboxSignal signal) : SaveChangesInterceptor
{
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Nudge(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Nudge(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// Wakes the dispatcher when outbox rows were written (it retries every second anyway, e.g. before a commit).
    private void Nudge(DbContext? context)
    {
        if (context?.Database.CurrentTransaction is null)
        {
            signal.Nudge();
        }
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddOutboxRows(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddOutboxRows(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void AddOutboxRows(DbContext? context)
    {
        if (context is not StoreDbContext db)
        {
            return;
        }

        var aggregates = db.ChangeTracker.Entries<AggregateRoot>().Select(e => e.Entity).Where(a => a.DomainEvents.Count > 0).ToList();
        var now = clock.UtcNow;
        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                db.OutboxMessages.Add(OutboxSerializer.ToMessage(domainEvent, now));
            }

            aggregate.ClearDomainEvents();
        }
    }
}
