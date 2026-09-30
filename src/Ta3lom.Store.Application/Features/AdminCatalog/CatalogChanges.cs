using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.AdminCatalog;

/// Debounced ta3lom-web rebuild (plan 14 S-35): a publish or archive queues `CatalogChanged` unless one is already waiting in
/// the outbox, so a burst of catalogue edits triggers one rebuild. Call before the unit of work saves.
internal sealed class CatalogChanges(IApplicationDbContext db, IOutbox outbox)
{
    public async Task NotifyAsync(Guid productId, string change, CancellationToken ct)
    {
        if (!await db.OutboxMessages.AnyAsync(m => m.Type == nameof(CatalogChanged) && m.ProcessedAt == null, ct))
        {
            outbox.Enqueue(new CatalogChanged(productId, change));
        }
    }
}
