using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.AdminOps;

namespace Ta3lom.Store.Application.Features.AdminOps;

/// `GET /v1/admin/reports/{name}` (#117, FR-S75, 11 §9.16): `sales`, `vat`, `gifts`, `zakat`, `allocation`, `refunds`,
/// `shipments` — read-only queries over the views and ledgers for a Riyadh date range (default the last 30 days, at most
/// 366 days), as JSON rows or CSV (UTF-8 with BOM so Excel keeps the Arabic).
public sealed record ReportQuery(string Name, DateOnly? From, DateOnly? To, Guid? ProductId) : IQuery<ReportResponse>;

public sealed record ReportCsvQuery(string Name, DateOnly? From, DateOnly? To, Guid? ProductId) : IQuery<ReportFile>;

internal sealed class ReportHandlers(IApplicationDbContext db, IClock clock) : IQueryHandler<ReportQuery, ReportResponse>, IQueryHandler<ReportCsvQuery, ReportFile>
{
    public static readonly string[] Names = ["sales", "vat", "gifts", "zakat", "allocation", "refunds", "shipments"];

    public async Task<Result<ReportResponse>> HandleAsync(ReportQuery query, CancellationToken ct = default)
    {
        var table = await BuildAsync(query.Name, query.From, query.To, query.ProductId, ct);
        if (table.IsFailure)
        {
            return table.Error;
        }

        var (from, to, columns, rows) = table.Value;
        return new ReportResponse(query.Name, from, to,
            [.. rows.Select(r => (IReadOnlyDictionary<string, object?>)columns.Select((c, i) => (c.Json, r[i])).ToDictionary(x => x.Json, x => x.Item2))]);
    }

    public async Task<Result<ReportFile>> HandleAsync(ReportCsvQuery query, CancellationToken ct = default)
    {
        var table = await BuildAsync(query.Name, query.From, query.To, query.ProductId, ct);
        if (table.IsFailure)
        {
            return table.Error;
        }

        var (from, to, columns, rows) = table.Value;
        var csv = new StringBuilder();
        csv.AppendJoin(',', columns.Select(c => c.Csv)).Append("\r\n");
        foreach (var r in rows)
        {
            csv.AppendJoin(',', r.Select(Cell)).Append("\r\n");
        }

        return new ReportFile([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())],
            string.Create(CultureInfo.InvariantCulture, $"{query.Name}-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.csv"));
    }

    private static string Cell(object? v)
    {
        var s = v switch
        {
            null => string.Empty,
            DateTimeOffset d => d.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? string.Empty,
        };
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : s;
    }

    private async Task<Result<(DateOnly From, DateOnly To, (string Json, string Csv)[] Columns, List<object?[]> Rows)>> BuildAsync(
        string name, DateOnly? fromDay, DateOnly? toDay, Guid? productId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.ToOffset(DashboardQueryHandler.Riyadh).DateTime);
        var to = toDay ?? today;
        var from = fromDay ?? to.AddDays(-29);
        if (!Names.Contains(name, StringComparer.Ordinal))
        {
            return Error.NotFound(ErrorCodes.NotFound, "Unknown report.");
        }

        if (from > to || to.DayNumber - from.DayNumber > 365)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["from"] = ["from ≤ to, at most 366 days"] });
        }

        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), DashboardQueryHandler.Riyadh).ToUniversalTime();
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), DashboardQueryHandler.Riyadh).ToUniversalTime();
        static (string, string) C(string json, string csv) => (json, csv);

        switch (name)
        {
            case "sales":
                {
                    var rows = await db.SalesDaily.AsNoTracking().Where(v => v.Kind == "buy" && v.Day >= from && v.Day <= to)
                        .OrderBy(v => v.Day).ThenBy(v => v.ProductType).ToListAsync(ct);
                    return (from, to, [C("day", "day"), C("productType", "product_type"), C("orders", "orders"), C("gross", "gross"), C("vat", "vat"), C("refunded", "refunded")],
                        rows.Select(v => new object?[] { v.Day, v.ProductType, v.Orders, M(v.Gross), M(v.Vat), M(v.Refunded) }).ToList());
                }

            case "vat":
                {
                    var orders = await db.Orders.AsNoTracking().Where(o => o.PaidAt >= start && o.PaidAt < end)
                        .Select(o => new { o.PaidAt, o.PurchasesTotal, o.ShippingTotal, o.VatTotal }).ToListAsync(ct);
                    return (from, to, [C("day", "day"), C("net", "net"), C("vat", "vat"), C("gross", "gross")],
                        orders.GroupBy(o => Day(o.PaidAt!.Value)).OrderBy(g => g.Key).Select(g =>
                        {
                            var gross = g.Select(o => o.PurchasesTotal + o.ShippingTotal).Sum();
                            var vat = g.Select(o => o.VatTotal).Sum();
                            return new object?[] { g.Key, (gross - vat).ToWire(), vat.ToWire(), gross.ToWire() };
                        }).ToList());
                }

            case "gifts":
                {
                    var lines = await (from l in db.OrderLines.AsNoTracking()
                                       join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
                                       join p in db.Products.AsNoTracking() on l.ProductId equals p.Id
                                       where l.Kind != LineKind.Buy && o.PaidAt >= start && o.PaidAt < end && (productId == null || l.ProductId == productId)
                                       select new { p.Slug, l.Kind, l.Intent, l.Amount, l.RefundedAmount }).ToListAsync(ct);
                    return (from, to, [C("product", "product"), C("kind", "kind"), C("intent", "intent"), C("lines", "lines"), C("amount", "amount"), C("refunded", "refunded")],
                        lines.GroupBy(l => (l.Slug, l.Kind, l.Intent)).OrderBy(g => g.Key.Slug, StringComparer.Ordinal).ThenBy(g => g.Key.Kind)
                            .Select(g => new object?[] { g.Key.Slug, g.Key.Kind.ToWire(), g.Key.Intent?.ToWire(), g.Count(), g.Select(l => l.Amount).Sum().ToWire(),
                            g.Select(l => l.RefundedAmount).Sum().ToWire() }).ToList());
                }

            case "zakat":
                {
                    var entries = await (from z in db.ZakatLedger.AsNoTracking()
                                         join p in db.Products.AsNoTracking() on z.ProductId equals p.Id
                                         where z.At >= start && z.At < end && (productId == null || z.ProductId == productId)
                                         select new { p.Slug, z.Direction, z.Amount }).ToListAsync(ct);
                    return (from, to, [C("product", "product"), C("received", "received"), C("disbursed", "disbursed"), C("refunded", "refunded"), C("balance", "balance")],
                        entries.GroupBy(e => e.Slug).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
                        {
                            Money S(ZakatDirection d) => g.Where(e => e.Direction == d).Select(e => e.Amount).Sum();
                            var (i, o, r) = (S(ZakatDirection.In), S(ZakatDirection.Out), S(ZakatDirection.Refund));
                            return new object?[] { g.Key, i.ToWire(), o.ToWire(), r.ToWire(), new Money(i.Amount - o.Amount - r.Amount).ToWire() };
                        }).ToList());
                }

            case "allocation":
                {
                    var rows = await db.AllocationBacklog.AsNoTracking().Where(v => productId == null || v.ProductId == productId).OrderBy(v => v.Slug).ToListAsync(ct);
                    return (from, to, [C("product", "product"), C("awaiting", "awaiting"), C("overdue30", "overdue_30"), C("allocated", "allocated"), C("completed", "completed"),
                        C("oldestAwaiting", "oldest_awaiting")],
                        rows.Select(v => new object?[] { v.Slug, v.Awaiting, v.Overdue30, v.Allocated, v.Completed, v.OldestAwaiting }).ToList());
                }

            case "refunds":
                {
                    var rows = await (from r in db.Refunds.AsNoTracking()
                                      join o in db.Orders.AsNoTracking() on r.OrderId equals o.Id
                                      where r.CreatedAt >= start && r.CreatedAt < end
                                      orderby r.CreatedAt
                                      select new { r.Id, r.CreatedAt, o.Reference, r.Amount, r.Status, r.RequestedBy }).ToListAsync(ct);
                    var ids = rows.Select(r => r.Id).ToList();
                    var counts = await db.RefundLines.AsNoTracking().Where(l => ids.Contains(l.RefundId)).GroupBy(l => l.RefundId)
                        .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
                    return (from, to, [C("date", "date"), C("order", "order"), C("lines", "lines"), C("amount", "amount"), C("status", "status"), C("requestedBy", "requested_by")],
                        rows.Select(r => new object?[] { Day(r.CreatedAt), r.Reference, counts.GetValueOrDefault(r.Id), r.Amount.ToWire(), r.Status.ToWire(), r.RequestedBy }).ToList());
                }

            default:
                {
                    var rows = await (from s in db.Shipments.AsNoTracking()
                                      join o in db.Orders.AsNoTracking() on s.OrderId equals o.Id
                                      where o.PaidAt >= start && o.PaidAt < end
                                      orderby o.PaidAt
                                      select new { o.Reference, s.Status, s.Carrier, s.TrackingNo, s.ShippedAt, s.DeliveredAt }).ToListAsync(ct);
                    return (from, to, [C("order", "order"), C("status", "status"), C("carrier", "carrier"), C("tracking", "tracking"), C("shippedAt", "shipped_at"),
                        C("deliveredAt", "delivered_at")],
                        rows.Select(r => new object?[] { r.Reference, r.Status.ToWire(), r.Carrier, r.TrackingNo, r.ShippedAt, r.DeliveredAt }).ToList());
                }
        }
    }

    private static string M(decimal? v) => Money.Round(v ?? 0m).ToWire();

    private static DateOnly Day(DateTimeOffset at) => DateOnly.FromDateTime(at.ToOffset(DashboardQueryHandler.Riyadh).DateTime);
}
