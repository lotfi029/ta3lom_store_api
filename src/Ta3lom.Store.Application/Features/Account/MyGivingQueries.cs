using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Account;

namespace Ta3lom.Store.Application.Features.Account;

/// `GET /v1/me/giving` (#33, FR-S25): totals and the gift lines of the customer's paid orders, newest first — unit statuses,
/// the pooled share ("x of 1 seat", sweep date), the latest cause update, the dedication. Never a beneficiary identity (RUL-50).
public sealed record MyGivingQuery(int Page, int PageSize) : IQuery<GivingResponse>;

/// `GET /v1/me/giving/lines/{orderLineId}` (#34): the seats of one line (status, region and group only, timeline) or the
/// cause's updates (public and donor-only, RUL-54).
public sealed record MyGivingLineQuery(Guid OrderLineId) : IQuery<GivingLineDetail>;

internal sealed class MyGivingQueryHandlers(IApplicationDbContext db, ICurrentActor actor, IWebLinks links) :
    IQueryHandler<MyGivingQuery, GivingResponse>, IQueryHandler<MyGivingLineQuery, GivingLineDetail>
{
    internal static readonly OrderStatus[] Settled = [OrderStatus.Paid, OrderStatus.Fulfilled, OrderStatus.PartiallyRefunded, OrderStatus.Refunded];

    private Guid Me => actor.Id ?? throw new InvalidOperationException("customer route without a customer");

    public async Task<Result<GivingResponse>> HandleAsync(MyGivingQuery query, CancellationToken ct = default)
    {
        var lines = from l in db.OrderLines.AsNoTracking()
                    join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
                    where o.CustomerId == Me && Settled.Contains(o.Status) && l.Kind != LineKind.Buy
                    select new { l, o.Reference, o.PaidAt, o.Locale };
        var all = await lines.Select(x => new { x.l.Amount, x.l.RefundedAmount, x.l.Intent, x.l.Kind, x.l.Mode, x.l.Quantity, x.l.Id }).ToListAsync(ct);
        var allIds = all.Select(a => a.Id).ToList();
        var dedicationCount = await db.Dedications.CountAsync(d => allIds.Contains(d.OrderLineId), ct);
        var totals = new GivingTotals(
            all.Select(a => a.Amount - a.RefundedAmount).Sum().ToWire(),
            all.Where(a => a.Intent == GiftIntent.Zakat).Select(a => a.Amount).Sum().ToWire(),
            all.Where(a => a.Kind == LineKind.Sponsor && a.Mode == LineMode.Units).Sum(a => a.Quantity),
            all.Count(a => a.Kind == LineKind.Sponsor && a.Mode == LineMode.Amount),
            dedicationCount);

        var page = new PageRequest(query.Page, query.PageSize);
        var rows = await lines.OrderByDescending(x => x.PaidAt).ThenBy(x => x.l.LineNo).Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        var ids = rows.Select(r => r.l.Id).ToList();
        var productIds = rows.Select(r => r.l.ProductId).Distinct().ToList();
        var offerIds = rows.Where(r => r.l.SponsorshipOfferId != null).Select(r => r.l.SponsorshipOfferId!.Value).Distinct().ToList();

        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Slug, ct);
        var offers = await db.SponsorshipOffers.AsNoTracking().Where(o => offerIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var unitCounts = await db.SponsoredUnits.AsNoTracking().Where(u => u.OrderLineId != null && ids.Contains(u.OrderLineId.Value))
            .GroupBy(u => new { u.OrderLineId, u.Status }).Select(g => new { g.Key.OrderLineId, g.Key.Status, N = g.Count() }).ToListAsync(ct);
        var contributions = await db.PoolContributions.AsNoTracking().Where(c => ids.Contains(c.OrderLineId)).ToDictionaryAsync(c => c.OrderLineId, ct);
        var contributionIds = contributions.Values.Select(c => c.Id).ToList();
        var converted = (await db.UnitFundings.AsNoTracking().Where(f => contributionIds.Contains(f.PoolContributionId)).ToListAsync(ct))
            .GroupBy(f => f.PoolContributionId).ToDictionary(g => g.Key, g => g.Select(f => f.Amount).Sum());
        var updates = await db.CauseUpdates.AsNoTracking().Where(u => productIds.Contains(u.ProductId))
            .OrderByDescending(u => u.OccurredOn).ThenByDescending(u => u.CreatedAt).ToListAsync(ct);
        var dedications = await db.Dedications.AsNoTracking().Where(d => ids.Contains(d.OrderLineId)).ToDictionaryAsync(d => d.OrderLineId, ct);

        var items = rows.Select(r =>
        {
            var l = r.l;
            GivingUnits? units = null;
            if (l.Kind == LineKind.Sponsor && l.Mode == LineMode.Units)
            {
                int N(params UnitStatus[] s) => unitCounts.Where(u => u.OrderLineId == l.Id && s.Contains(u.Status)).Sum(u => u.N);
                units = new GivingUnits(N(UnitStatus.AwaitingAllocation, UnitStatus.Lapsed), N(UnitStatus.Allocated), N(UnitStatus.Completed));
            }

            GivingPooled? pooled = null;
            if (contributions.TryGetValue(l.Id, out var c) && l.SponsorshipOfferId is { } oid && offers.TryGetValue(oid, out var offer))
            {
                var conv = converted.TryGetValue(c.Id, out var v) ? v : Money.Zero;
                pooled = new GivingPooled(conv.ToWire(), c.Remaining.ToWire(), offer.SeatPrice.ToWire(),
                    Math.Round(c.Amount.Amount / offer.SeatPrice.Amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture),
                    c.SweptToFundAt);
            }

            var group = l.SponsorshipOfferId is { } gid && offers.TryGetValue(gid, out var o) ? o : null;
            var latest = l.Kind == LineKind.Donate ? updates.FirstOrDefault(u => u.ProductId == l.ProductId) : null;
            var dedication = dedications.TryGetValue(l.Id, out var d)
                ? new GivingDedication(d.ToName, links.Card(r.Locale, d.CardToken), d.SmsStatus.ToWire())
                : null;
            return new GivingItem(l.Id, r.Reference, r.PaidAt, l.Kind.ToWire(), l.Mode.ToWire(), products[l.ProductId], l.TitleAr, l.TitleEn, l.Quantity,
                l.Amount.ToWire(), l.Intent?.ToWire(), units, group?.GroupLabelAr, group?.GroupLabelEn, pooled,
                latest is null ? null : new GivingLatestUpdate(latest.TitleAr, latest.TitleEn, latest.OccurredOn), dedication);
        }).ToList();
        return new GivingResponse(totals, items, all.Count, page.SafePage, page.Take);
    }

    public async Task<Result<GivingLineDetail>> HandleAsync(MyGivingLineQuery query, CancellationToken ct = default)
    {
        var line = await (from l in db.OrderLines.AsNoTracking()
                          join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
                          where l.Id == query.OrderLineId && o.CustomerId == Me && Settled.Contains(o.Status) && l.Kind != LineKind.Buy
                          select l).FirstOrDefaultAsync(ct);
        if (line is null)
        {
            return GeneralErrors.NotFound;
        }

        var group = line.SponsorshipOfferId is { } oid
            ? await db.SponsorshipOffers.AsNoTracking().Where(o => o.Id == oid).Select(o => o.GroupLabelAr).FirstOrDefaultAsync(ct)
            : null;

        // Full units of the line, or the pooled units this line's contribution helped fund (with its share).
        var contribution = await db.PoolContributions.AsNoTracking().FirstOrDefaultAsync(c => c.OrderLineId == line.Id, ct);
        var shares = contribution is null
            ? new Dictionary<Guid, Money>()
            : await db.UnitFundings.AsNoTracking().Where(f => f.PoolContributionId == contribution.Id).ToDictionaryAsync(f => f.SponsoredUnitId, f => f.Amount, ct);
        var shareIds = shares.Keys.ToList();
        var units = await (from u in db.SponsoredUnits.AsNoTracking()
                           join b in db.Beneficiaries.AsNoTracking() on u.BeneficiaryId equals b.Id into bs
                           from b in bs.DefaultIfEmpty()
                           where u.OrderLineId == line.Id || shareIds.Contains(u.Id)
                           orderby u.FundedAt, u.Id
                           select new { u.Id, u.Status, u.FundedAt, u.AllocatedAt, u.CompletedAt, Region = b == null ? null : b.Region })   // region only (RUL-50)
            .ToListAsync(ct);
        var unitIds = units.Select(u => u.Id).ToList();
        var events = await db.SponsoredUnitEvents.AsNoTracking().Where(e => unitIds.Contains(e.UnitId)).OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => new { e.UnitId, e.ToStatus, e.At }).ToListAsync(ct);

        var views = units.Select((u, i) => new GivingUnitView(
            "U-" + (i + 1).ToString(CultureInfo.InvariantCulture),
            u.Status.ToWire(),
            u.Status is UnitStatus.Allocated or UnitStatus.Completed ? u.Region : null,
            group, u.FundedAt, u.AllocatedAt, u.CompletedAt,
            shares.TryGetValue(u.Id, out var share) ? share.ToWire() : null,
            [.. events.Where(e => e.UnitId == u.Id).Select(e => new GivingUnitStep(e.ToStatus, e.At))])).ToList();

        var updates = line.Kind == LineKind.Donate
            ? await db.CauseUpdates.AsNoTracking().Where(u => u.ProductId == line.ProductId).OrderByDescending(u => u.OccurredOn)
                .Select(u => new GivingUpdateView(u.TitleAr, u.TitleEn, u.BodyAr, u.BodyEn, u.UnitsDelivered, u.OccurredOn)).ToListAsync(ct)
            : [];
        return new GivingLineDetail(line.Id, line.Kind.ToWire(), views, updates);
    }
}
