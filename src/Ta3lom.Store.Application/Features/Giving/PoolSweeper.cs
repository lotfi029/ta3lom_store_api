using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Giving;

/// RUL-46 (09 §5.5): sweeps an offer's pool remainder to the general education fund inside the caller's transaction — pool
/// and open contributions locked, `goal_adjustment` pair (`pool_sweep_out` / `pool_sweep_in`, shared `pair_id`), both goal
/// counters moved, `swept_to_fund_at` set, `PoolSwept` queued. Zakat is never swept. Nothing to sweep, or no general
/// fund configured → null (nothing changes).
internal sealed class PoolSweeper(IApplicationDbContext db, ISponsorshipPoolRepository pools, IGenericRepository<GoalAdjustment> adjustments,
    IGoalProgressRepository goals, IOutbox outbox)
{
    public async Task<(Money Amount, Guid PairId)?> SweepAsync(Guid offerId, Guid productId, string actor, DateTimeOffset now, CancellationToken ct)
    {
        var fund = await db.CauseDetails.AsNoTracking().Where(c => c.IsGeneralFund).Select(c => (Guid?)c.ProductId).FirstOrDefaultAsync(ct);
        if (fund is not { } generalFundId || generalFundId == productId)
        {
            return null;
        }

        var pool = await pools.LockAsync(offerId, now, ct);
        var open = await pools.LockOpenContributionsAsync(offerId, ct);
        var amount = pool.Sweep(open, now);
        if (amount == Money.Zero)
        {
            return null;
        }

        var pair = Guid.CreateVersion7();
        adjustments.Add(GoalAdjustment.PoolSweepOut(productId, amount, offerId, pair, actor, now));
        adjustments.Add(GoalAdjustment.PoolSweepIn(generalFundId, amount, offerId, pair, actor, now));
        await goals.AddGiftAsync(productId, new Money(-amount.Amount), 0, 0, Money.Zero, now, ct);
        await goals.AddGiftAsync(generalFundId, amount, 0, 0, Money.Zero, now, ct);
        outbox.Enqueue(new PoolSwept(offerId, productId, generalFundId, pair, amount.ToWire()));
        return (amount, pair);
    }
}
