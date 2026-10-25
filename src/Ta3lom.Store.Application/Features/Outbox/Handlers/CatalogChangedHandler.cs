using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Outbox.Handlers;

/// A catalogue write → one rebuild of the static site. Unclaimed pending changes are absorbed and only the newest pending one
/// triggers, so a burst of edits sends one `repository_dispatch`; when the rebuild is not configured the trigger logs and the
/// message is done.
internal sealed class CatalogChangedHandler(ICatalogChangeDebounce debounce, ISiteRebuildTrigger trigger, IClock clock) : OutboxHandler<CatalogChanged>
{
    protected override async Task HandleAsync(CatalogChanged message, OutboxEnvelope envelope, CancellationToken ct)
    {
        if (await debounce.AbsorbAsync(envelope.Id, clock.UtcNow, ct))
        {
            await trigger.TriggerAsync($"{message.Entity}.{message.Change}", ct);
        }
    }
}
