using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Application.Features.Settlement.Effects;

/// RUL-53, RUL-40: a donation adds to the cause's counters (applied after the loop by `SettlementLedger`) and has no
/// per-unit rows; the line is `received` at once.
internal sealed class DonateLineEffect : ILineSettlementEffect
{
    public bool Handles(OrderLine line, SettlementContext context) => line.Kind == LineKind.Donate;

    public Task ApplyAsync(OrderLine line, SettlementContext context, CancellationToken ct)
    {
        line.SetFulfilment(FulfilmentStatus.Received);
        return Task.CompletedTask;
    }
}
