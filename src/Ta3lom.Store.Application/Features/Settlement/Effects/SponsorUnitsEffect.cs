using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Application.Features.Settlement.Effects;

/// RUL-43, RUL-44: a `units`-mode sponsor line of quantity N creates exactly N `full` units (`awaiting_allocation`, each
/// linked to the line, priced at the line's unit price) with their funding event; the target may be exceeded. Zakat intent
/// marks every unit (RUL-47d). The line is `funded` (RUL-40); `funded_units` is counted by `SettlementLedger`.
internal sealed class SponsorUnitsEffect(IGenericRepository<SponsoredUnit> units, IGenericRepository<SponsoredUnitEvent> events)
    : ILineSettlementEffect
{
    public const string Actor = "settlement";

    public bool Handles(OrderLine line, SettlementContext context) => line.Kind == LineKind.Sponsor && line.Mode == LineMode.Units;

    public Task ApplyAsync(OrderLine line, SettlementContext context, CancellationToken ct)
    {
        var offerId = line.SponsorshipOfferId ?? throw new DomainException("settlement.offer_missing", $"Sponsor line {line.Id} has no offer.");
        var isZakat = line.Intent == GiftIntent.Zakat;
        for (var i = 0; i < line.Quantity; i++)
        {
            var unit = SponsoredUnit.Full(line.ProductId, offerId, line.Id, line.UnitPrice, isZakat, context.Now);
            units.Add(unit);
            events.Add(SponsoredUnitEvent.Transition(unit.Id, null, UnitStatus.AwaitingAllocation, Actor, context.Now));
        }

        line.SetFulfilment(FulfilmentStatus.Funded);
        return Task.CompletedTask;
    }
}
