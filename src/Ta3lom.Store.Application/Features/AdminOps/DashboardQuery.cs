using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.AdminOps;

namespace Ta3lom.Store.Application.Features.AdminOps;

/// `GET /v1/admin/dashboard` (#53, FR-S74): sales and gifts for today / 7 / 30 Riyadh days (by `paid_at`), orders waiting for
/// payment or in review, top products (30 days), open campaigns with their progress, the allocation backlog and the open
/// alerts (overdue units, dead outbox messages, failing integrity checks).
public sealed record DashboardQuery : IQuery<DashboardResponse>;

internal sealed class DashboardQueryHandler(IApplicationDbContext db, IOperationsStatus ops, IClock clock) : IQueryHandler<DashboardQuery, DashboardResponse>
{
    internal static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);

    public async Task<Result<DashboardResponse>> HandleAsync(DashboardQuery query, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now.ToOffset(Riyadh).DateTime);
        DateTimeOffset Start(int daysBack) => new DateTimeOffset(today.AddDays(-daysBack).ToDateTime(TimeOnly.MinValue), Riyadh).ToUniversalTime();
        var from30 = Start(29);
        var lines = await (from l in db.OrderLines.AsNoTracking()
                           join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
                           join p in db.Products.AsNoTracking() on l.ProductId equals p.Id
                           where o.PaidAt != null && o.PaidAt >= from30
                           select new { o.PaidAt, l.Kind, l.Amount, p.Slug }).ToListAsync(ct);

        PeriodTotals Totals(bool buy) => new(
            Sum(buy, Start(0)), Sum(buy, Start(6)), Sum(buy, from30));
        string Sum(bool buy, DateTimeOffset from) =>
            lines.Where(l => (l.Kind == LineKind.Buy) == buy && l.PaidAt >= from).Select(l => l.Amount).Sum().ToWire();

        var top = lines.GroupBy(l => l.Slug).Select(g => new { Slug = g.Key, Amount = g.Select(l => l.Amount).Sum() })
            .OrderByDescending(g => g.Amount.Amount).Take(5).Select(g => new TopProduct(g.Slug, g.Amount.ToWire())).ToList();

        var campaigns = (await db.Goals.AsNoTracking().Where(g => g.EndsAt != null && g.EndsAt > now).OrderBy(g => g.EndsAt).Take(10).ToListAsync(ct))
            .Select(g => new CampaignProgress(g.Slug, Percent(g), g.EndsAt)).ToList();

        var backlog = await db.AllocationBacklog.AsNoTracking().ToListAsync(ct);
        var allocation = new AllocationSummary(backlog.Sum(b => b.Awaiting), backlog.Sum(b => b.Overdue30), backlog.Min(b => b.OldestAwaiting));
        var failing = (await ops.RunIntegrityChecksAsync(ct)).Count(c => c.Rows > 0);

        return new DashboardResponse(Totals(buy: true), Totals(buy: false),
            await db.Orders.CountAsync(o => o.Status == OrderStatus.PendingPayment, ct),
            await db.Orders.CountAsync(o => o.Status == OrderStatus.PaymentReview, ct),
            top, campaigns, allocation,
            [
                new DashboardAlert("allocation.overdue", allocation.Overdue30),
                new DashboardAlert("outbox.dead", await ops.DeadOutboxCountAsync(ct)),
                new DashboardAlert("integrity.failed", failing),
            ]);
    }

    private static int Percent(Domain.ReadModels.GoalView g)
    {
        if (g.TargetAmount is > 0)
        {
            return (int)Math.Floor((g.RaisedAmount ?? 0m) * 100m / g.TargetAmount.Value);
        }

        return g.TargetUnits is > 0 ? (int)Math.Floor((g.FundedUnits ?? 0) * 100m / g.TargetUnits.Value) : 0;
    }
}
