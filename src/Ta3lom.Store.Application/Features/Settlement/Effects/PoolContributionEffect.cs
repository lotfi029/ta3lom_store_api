using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Application.Features.Settlement.Effects;

/// RUL-45 (database/01 §4.5, 09 §5.5): an `amount`-mode sponsor line becomes a `pool_contribution`; with the pool row and
/// the offer's open contributions locked, `SponsorshipPool.Add` converts whole seats FIFO into `pooled` units with their
/// `unit_funding` rows (zakat propagates to the unit, RUL-47d). New pooled units add to `funded_units`; the money itself is
/// counted once by `SettlementLedger`. The line is `funded` (RUL-40).
internal sealed class PoolContributionEffect(
    IApplicationDbContext db,
    ISponsorshipPoolRepository pools,
    IGenericRepository<PoolContribution> contributions,
    IGenericRepository<SponsoredUnit> units,
    IGenericRepository<UnitFunding> fundings,
    IGenericRepository<SponsoredUnitEvent> events,
    IGoalProgressRepository goals) : ILineSettlementEffect
{
    public bool Handles(OrderLine line, SettlementContext context) => line.Kind == LineKind.Sponsor && line.Mode == LineMode.Amount;

    public async Task ApplyAsync(OrderLine line, SettlementContext context, CancellationToken ct)
    {
        var offerId = line.SponsorshipOfferId ?? throw new DomainException("settlement.offer_missing", $"Sponsor line {line.Id} has no offer.");
        var seatPrice = await db.SponsorshipOffers.AsNoTracking().Where(o => o.Id == offerId).Select(o => o.SeatPrice).SingleAsync(ct);

        var pool = await pools.LockAsync(offerId, context.Now, ct);                        // pool row first, then contributions
        var contribution = PoolContribution.FromLine(offerId, line.Id, line.Amount, line.Intent == GiftIntent.Zakat, context.Now);
        contributions.Add(contribution);
        var open = await pools.LockOpenContributionsAsync(offerId, ct);                     // includes the new one

        var result = pool.Add(contribution, open, line.ProductId, seatPrice, context.Now);
        foreach (var unit in result.Units)
        {
            units.Add(unit);
            events.Add(SponsoredUnitEvent.Transition(unit.Id, null, UnitStatus.AwaitingAllocation, SponsorUnitsEffect.Actor, context.Now));
        }

        fundings.AddRange(result.Fundings);
        if (result.Units.Count > 0)
        {
            await goals.AddGiftAsync(line.ProductId, Money.Zero, result.Units.Count, donors: 0, Money.Zero, context.Now, ct);
        }

        line.SetFulfilment(FulfilmentStatus.Funded);
    }
}
