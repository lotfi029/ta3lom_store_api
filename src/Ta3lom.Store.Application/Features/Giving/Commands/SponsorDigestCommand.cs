using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Features.Account;

namespace Ta3lom.Store.Application.Features.Giving.Commands;

/// `SponsorDigest` job (1st of the month 09:00 Riyadh, 09 §6, database/03 §5; RUL-63, FR-S25): every active customer with a
/// settled gift line gets one outbox `SponsorDigest` covering the changes since their last digest (unit allocations,
/// completions and lapses with group and region only, pooled shares, sweeps, cause updates). Sponsors with no change are
/// skipped; a second run in the same Riyadh month queues nothing. Returns the number of digests queued.
public sealed record SponsorDigestCommand : ICommand<int>;

internal sealed class SponsorDigestCommandHandler(IApplicationDbContext db, SponsorDigestReader reader, IOutbox outbox, IUnitOfWork uow, IClock clock)
    : ICommandHandler<SponsorDigestCommand, int>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<int>> HandleAsync(SponsorDigestCommand command, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var local = now.ToOffset(SponsorDigestReader.Riyadh);
        var period = local.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var firstDigestSince = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, SponsorDigestReader.Riyadh).AddMonths(-1).ToUniversalTime();

        var sponsors = await (from c in db.Customers.AsNoTracking()
                              where c.Status == CustomerStatus.Active
                                    && db.Orders.Any(o => o.CustomerId == c.Id && MyGivingQueryHandlers.Settled.Contains(o.Status)
                                                          && db.OrderLines.Any(l => l.OrderId == o.Id && l.Kind != LineKind.Buy))
                              orderby c.Id
                              select c.Id).ToListAsync(ct);

        // The digest rows are the "last digest" record (Retention keeps them 13 months).
        var last = (await db.OutboxMessages.AsNoTracking().Where(m => m.Type == nameof(Domain.Events.SponsorDigest)).Select(m => m.Payload).ToListAsync(ct))
            .Select(p => JsonSerializer.Deserialize<Domain.Events.SponsorDigest>(p, Json))
            .OfType<Domain.Events.SponsorDigest>()
            .GroupBy(d => d.CustomerId)
            .ToDictionary(g => g.Key, g => g.MaxBy(d => d.Until)!);

        var queued = 0;
        foreach (var customerId in sponsors)
        {
            var previous = last.GetValueOrDefault(customerId);
            if (previous?.Period == period)
            {
                continue;                                                                  // once per month
            }

            var since = previous?.Until ?? firstDigestSince;
            var content = await reader.ReadAsync(customerId, since, now, ct);
            if (content.IsEmpty)
            {
                continue;                                                                  // nothing changed
            }

            outbox.Enqueue(new Domain.Events.SponsorDigest(customerId, period, since, now));
            queued++;
        }

        if (queued > 0)
        {
            await uow.SaveChangesAsync(ct);
        }

        return queued;
    }
}

internal sealed record DigestUnitChange(Guid ProductId, string? GroupAr, string? GroupEn, string ToStatus, string? Region);

internal sealed record DigestPooledShare(Guid ProductId, Money Share, Money SeatPrice);

internal sealed record DigestSweep(Guid ProductId, Money Amount);

internal sealed record DigestUpdate(Guid ProductId, string TitleAr, string? TitleEn, DateOnly OccurredOn);

internal sealed record DigestContent(
    IReadOnlyList<DigestUnitChange> Units,
    IReadOnlyList<DigestPooledShare> Pooled,
    IReadOnlyList<DigestSweep> Sweeps,
    IReadOnlyList<DigestUpdate> Updates,
    IReadOnlyDictionary<Guid, (string Ar, string? En)> Products)
{
    public bool IsEmpty => Units.Count == 0 && Pooled.Count == 0 && Sweeps.Count == 0 && Updates.Count == 0;
}

/// What changed for one sponsor in `(since, until]`. Beneficiaries appear only as a region and the offer's group label
/// (RUL-50); never a name, code or mobile.
internal sealed class SponsorDigestReader(IApplicationDbContext db)
{
    internal static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);

    public async Task<DigestContent> ReadAsync(Guid customerId, DateTimeOffset since, DateTimeOffset until, CancellationToken ct)
    {
        var lines = await (from l in db.OrderLines.AsNoTracking()
                           join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
                           where o.CustomerId == customerId && MyGivingQueryHandlers.Settled.Contains(o.Status) && l.Kind != LineKind.Buy
                           select new { l.Id, l.ProductId, l.Kind }).ToListAsync(ct);
        var lineIds = lines.Select(l => l.Id).ToList();

        var contributions = await db.PoolContributions.AsNoTracking().Where(c => lineIds.Contains(c.OrderLineId)).ToListAsync(ct);
        var contributionIds = contributions.Select(c => c.Id).ToList();
        var fundings = await db.UnitFundings.AsNoTracking().Where(f => contributionIds.Contains(f.PoolContributionId)).ToListAsync(ct);
        var pooledUnitIds = fundings.Select(f => f.SponsoredUnitId).Distinct().ToList();

        var units = await (from u in db.SponsoredUnits.AsNoTracking()
                           join b in db.Beneficiaries.AsNoTracking() on u.BeneficiaryId equals b.Id into bs
                           from b in bs.DefaultIfEmpty()
                           where (u.OrderLineId != null && lineIds.Contains(u.OrderLineId.Value)) || pooledUnitIds.Contains(u.Id)
                           select new { u.Id, u.ProductId, u.SponsorshipOfferId, u.UnitPrice, u.FundedAt, Region = b == null ? null : b.Region })
            .ToDictionaryAsync(u => u.Id, ct);
        var unitIds = units.Keys.ToList();

        string[] changes = [UnitStatus.Allocated.ToWire(), UnitStatus.Completed.ToWire(), UnitStatus.Lapsed.ToWire()];
        var events = await db.SponsoredUnitEvents.AsNoTracking()
            .Where(e => unitIds.Contains(e.UnitId) && e.At > since && e.At <= until && changes.Contains(e.ToStatus))
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => new { e.UnitId, e.ToStatus }).ToListAsync(ct);

        var offerIds = units.Values.Select(u => u.SponsorshipOfferId).Distinct().ToList();
        var groups = await db.SponsorshipOffers.AsNoTracking().Where(o => offerIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => new { o.GroupLabelAr, o.GroupLabelEn }, ct);
        var unitChanges = events.Select(e =>
        {
            var u = units[e.UnitId];
            var g = groups.GetValueOrDefault(u.SponsorshipOfferId);
            var showRegion = e.ToStatus != UnitStatus.Lapsed.ToWire();
            return new DigestUnitChange(u.ProductId, g?.GroupLabelAr, g?.GroupLabelEn, e.ToStatus, showRegion ? u.Region : null);
        }).ToList();

        var pooled = fundings.Where(f => units.TryGetValue(f.SponsoredUnitId, out var u) && u.FundedAt > since && u.FundedAt <= until)
            .Select(f => new DigestPooledShare(units[f.SponsoredUnitId].ProductId, f.Amount, units[f.SponsoredUnitId].UnitPrice))
            .ToList();

        var lineProducts = lines.ToDictionary(l => l.Id, l => l.ProductId);
        var sweeps = contributions.Where(c => c.SweptToFundAt > since && c.SweptToFundAt <= until)
            .Select(c => new DigestSweep(lineProducts[c.OrderLineId], c.Amount - fundings.Where(f => f.PoolContributionId == c.Id).Select(f => f.Amount).Sum()))
            .Where(s => s.Amount > Money.Zero)
            .ToList();

        var donated = lines.Where(l => l.Kind == LineKind.Donate).Select(l => l.ProductId).Distinct().ToList();
        var updates = await db.CauseUpdates.AsNoTracking()
            .Where(u => donated.Contains(u.ProductId) && u.CreatedAt > since && u.CreatedAt <= until)
            .OrderBy(u => u.OccurredOn).ThenBy(u => u.CreatedAt)
            .Select(u => new DigestUpdate(u.ProductId, u.TitleAr, u.TitleEn, u.OccurredOn)).ToListAsync(ct);

        var productIds = unitChanges.Select(c => c.ProductId).Concat(pooled.Select(p => p.ProductId)).Concat(sweeps.Select(s => s.ProductId))
            .Concat(updates.Select(u => u.ProductId)).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => (p.TitleAr, p.TitleEn), ct);
        return new DigestContent(unitChanges, pooled, sweeps, updates, products.ToDictionary(p => p.Key, p => (p.Value.TitleAr, p.Value.TitleEn)));
    }
}

/// The digest texts (ar / en): a plain-text e-mail body and a one-line SMS summary for sponsors without an e-mail address.
internal static class SponsorDigestText
{
    public static string EmailBody(DigestContent d, string locale, string? name, DateTimeOffset since, DateTimeOffset until, string link)
    {
        var en = locale == "en";
        var sb = new StringBuilder();
        var from = Day(since);
        var to = Day(until);
        sb.AppendLine(en
            ? (string.IsNullOrWhiteSpace(name) ? "Hello," : $"Hello {name},")
            : (string.IsNullOrWhiteSpace(name) ? "السلام عليكم،" : $"السلام عليكم {name}،"));
        sb.AppendLine(en ? $"Here is what your giving did from {from} to {to}:" : $"هذا ملخص أثر عطائك من {from} إلى {to}:");
        sb.AppendLine();

        foreach (var g in d.Units.GroupBy(u => (u.ProductId, u.ToStatus, u.GroupAr, u.GroupEn)))
        {
            var p = Title(d, g.Key.ProductId, en);
            var group = en ? g.Key.GroupEn ?? g.Key.GroupAr : g.Key.GroupAr;
            var head = group is null ? Quote(p, en) : $"{Quote(p, en)} ({group})";
            var regions = string.Join(en ? ", " : "، ", g.Select(u => u.Region).OfType<string>().Distinct());
            var tail = regions.Length == 0 ? string.Empty : " — " + regions;
            var n = g.Count().ToString(CultureInfo.InvariantCulture);
            sb.AppendLine(g.Key.ToStatus switch
            {
                "allocated" => en ? $"• {head}: {n} seat(s) allocated{tail}" : $"• {head}: خُصّص {n} مقعد{tail}",
                "completed" => en ? $"• {head}: {n} beneficiary(ies) completed the course{tail}" : $"• {head}: أتمّ {n} مستفيد الدورة{tail}",
                _ => en ? $"• {head}: {n} seat(s) returned for re-allocation after the start period ended"
                        : $"• {head}: أُعيد {n} مقعد للتخصيص بعد انتهاء مهلة البدء",
            });
        }

        foreach (var g in d.Pooled.GroupBy(p => p.ProductId))
        {
            var share = g.Select(p => p.Share).Sum();
            var seats = g.Sum(p => p.Share.Amount / p.SeatPrice.Amount);
            var of = Math.Round(seats, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
            var n = g.Count().ToString(CultureInfo.InvariantCulture);
            var p = Quote(Title(d, g.Key, en), en);
            sb.AppendLine(en
                ? $"• {p}: your contribution funded {share.ToWire()} SAR of {n} pooled seat(s) ({of} of a seat)"
                : $"• {p}: ساهمت بـ {share.ToWire()} ر.س في تمويل {n} مقعد مجمّع ({of} من مقعد)");
        }

        foreach (var s in d.Sweeps)
        {
            var p = Quote(Title(d, s.ProductId, en), en);
            sb.AppendLine(en
                ? $"• {p}: the campaign closed and the unused {s.Amount.ToWire()} SAR of your contribution went to the general fund"
                : $"• {p}: انتهت الحملة وحُوّل الجزء غير المستخدم من مساهمتك ({s.Amount.ToWire()} ر.س) إلى الصندوق العام");
        }

        foreach (var u in d.Updates)
        {
            var p = Quote(Title(d, u.ProductId, en), en);
            var title = en ? u.TitleEn ?? u.TitleAr : u.TitleAr;
            sb.AppendLine(CultureInfo.InvariantCulture, $"• {p}: {title} ({u.OccurredOn:yyyy-MM-dd})");
        }

        sb.AppendLine();
        sb.Append(en ? $"Details in your account: {link}" : $"التفاصيل في حسابك: {link}");
        return sb.ToString();
    }

    public static string Summary(DigestContent d, string locale)
    {
        var en = locale == "en";
        int N(string status) => d.Units.Count(u => u.ToStatus == status);
        List<string> parts = [];
        void Add(int n, string ar, string english)
        {
            if (n > 0)
            {
                parts.Add(n.ToString(CultureInfo.InvariantCulture) + " " + (en ? english : ar));
            }
        }

        Add(N("allocated"), "مقعد خُصّص", "seat(s) allocated");
        Add(N("completed"), "مستفيد أتمّ الدورة", "completed");
        Add(N("lapsed"), "مقعد أُعيد للتخصيص", "returned for re-allocation");
        Add(d.Pooled.Count, "مقعد مجمّع بمساهمتك", "pooled seat(s) funded");
        Add(d.Sweeps.Count, "مساهمة حُوّلت للصندوق العام", "swept to the general fund");
        Add(d.Updates.Count, "تحديث", "update(s)");
        return string.Join(en ? ", " : "، ", parts);
    }

    private static string Title(DigestContent d, Guid productId, bool en) =>
        d.Products.TryGetValue(productId, out var t) ? (en ? t.En ?? t.Ar : t.Ar) : string.Empty;

    private static string Quote(string title, bool en) => en ? $"\"{title}\"" : $"«{title}»";

    private static string Day(DateTimeOffset at) => at.ToOffset(SponsorDigestReader.Riyadh).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
