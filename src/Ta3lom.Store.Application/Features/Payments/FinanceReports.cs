using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.AdminOps;
using Ta3lom.Store.Application.Features.AdminOps;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Payments;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Payments;

/// `POST /v1/admin/orders/{reference}/receipt/resend` (plan §4; `staff:finance`, Idempotency-Key): the donor gets the paid
/// SMS and e-mail (with the DR PDF) again — a new outbox message, so a new send. On a refunded order (review 04 R-1) it is
/// the refund message `refund_processed` with the DRC instead: the DR alone is void. No DR → 404 `receipt.not_available`.
public sealed record ResendReceiptCommand(string Reference) : ICommand<AdminPaymentActionResponse>;

internal sealed class ResendReceiptCommandHandler(IOrderRepository orders, IReceiptRepository receipts, IRefundRepository refunds, IOutbox outbox,
    IAuditTrail audit, ICurrentActor actor, IUnitOfWork uow) : ICommandHandler<ResendReceiptCommand, AdminPaymentActionResponse>
{
    public async Task<Result<AdminPaymentActionResponse>> HandleAsync(ResendReceiptCommand command, CancellationToken ct = default)
    {
        var reference = Order.NormalizeReference(command.Reference);
        var order = await orders.FirstOrDefaultAsync(o => o.Reference == reference, ct);
        if (order is null)
        {
            return OrderErrors.NotFound;
        }

        if (await receipts.DonationReceiptForOrderAsync(order.Id, ct) is not { } receipt || order.Status is not (OrderStatus.Paid or OrderStatus.Refunded))
        {
            return PaymentErrors.ReceiptNotAvailable;
        }

        var sent = receipt;
        if (order.Status == OrderStatus.Refunded && receipt.VoidedByReceiptId is { } drcId
            && (await receipts.ForOrdersAsync([order.Id], ct)).FirstOrDefault(r => r.Id == drcId) is { } drc
            && (await refunds.ForOrderAsync(order.Id, ct)).LastOrDefault(r => r.Status == RefundStatus.Succeeded) is { } last)
        {
            outbox.Enqueue(new RefundProcessed(order.Id, order.Reference, last.Id, order.RefundedAmount.ToWire(), true, drc.Id));
            sent = drc;
        }
        else
        {
            outbox.Enqueue(new OrderPaid(order.Id, order.Reference, order.TotalAmount.ToWire(), receipt.Id));
        }

        audit.Record(AuditActions.ReceiptResent, "orders", order.Id.ToString(), actor.Name, new { order.Reference, receipt = sent.DisplayNumber });
        await uow.SaveChangesAsync(ct);
        return new AdminPaymentActionResponse(order.Reference, order.Status.ToWire(), order.TotalAmount.ToWire(), order.RefundedAmount.ToWire(),
            order.Version, sent.DisplayNumber);
    }
}

/// `GET /v1/admin/reports/donations.csv?from=YYYY-MM-DD&amp;to=YYYY-MM-DD` (plan §4; `staff:finance`): orders PAID in the
/// Riyadh date range (inclusive, at most 366 days), oldest first. UTF-8 with BOM (Excel opens the Arabic correctly);
/// donor mobile masked; every cell is CSV-escaped and neutralised against formula injection (`=`, `+`, `-`, `@`, tab, CR
/// at the start get a leading apostrophe). Review 04 S-12: streamed — read and written in pages of <see cref="PageSize"/>
/// orders (paid_at, reference order), each page's receipt numbers and payment methods looked up in a dictionary; a year of
/// donations never sits in memory at once.
public sealed record DonationsCsvQuery(DateOnly? From, DateOnly? To, int PageSize = DonationsCsvQuery.DefaultPageSize) : IQuery<CsvFile>
{
    public const int DefaultPageSize = 500;
}

/// The CSV is written straight to the response body by <see cref="WriteAsync"/>.
public sealed record CsvFile(Func<Stream, CancellationToken, Task> WriteAsync, string FileName);

internal sealed class DonationsCsvQueryHandler(IApplicationDbContext db, IClock clock) : IQueryHandler<DonationsCsvQuery, CsvFile>
{
    public const int MaxDays = 366;

    private static readonly string[] Header =
        ["reference", "paid_at_riyadh", "status", "donor_name", "donor_mobile", "lines", "amount", "refunded", "receipt_no", "method"];

    public Task<Result<CsvFile>> HandleAsync(DonationsCsvQuery query, CancellationToken ct = default)
    {
        var to = query.To ?? clock.RiyadhToday;
        var from = query.From ?? to.AddDays(-29);
        if (from > to || to.DayNumber - from.DayNumber + 1 > MaxDays)
        {
            return Task.FromResult<Result<CsvFile>>(Error.Validation(new Dictionary<string, string[]> { ["to"] = [$"from ≤ to, at most {MaxDays} days."] }));
        }

        var start = RiyadhDay.Start(from);
        var end = RiyadhDay.Start(to.AddDays(1));
        var name = string.Create(CultureInfo.InvariantCulture, $"donations-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.csv");
        var size = Math.Clamp(query.PageSize, 1, DonationsCsvQuery.DefaultPageSize);
        return Task.FromResult(Result.Success(new CsvFile((stream, token) => WriteAsync(stream, start, end, size, token), name)));
    }

    private async Task WriteAsync(Stream stream, DateTimeOffset start, DateTimeOffset end, int pageSize, CancellationToken ct)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 64 * 1024, leaveOpen: true);
        await writer.WriteAsync(string.Join(',', Header) + "\r\n");
        for (var skip = 0; ; skip += pageSize)
        {
            var orders = await db.Orders.AsNoTracking().Include(o => o.Lines).Where(o => o.PaidAt != null && o.PaidAt >= start && o.PaidAt < end)
                .OrderBy(o => o.PaidAt).ThenBy(o => o.Reference).Skip(skip).Take(pageSize).ToListAsync(ct);
            if (orders.Count == 0)
            {
                break;
            }

            var ids = orders.Select(o => o.Id).ToList();
            var receipts = await db.DonationReceipts.AsNoTracking().Where(r => ids.Contains(r.OrderId) && r.Series == ReceiptSeries.Dr)
                .ToDictionaryAsync(r => r.OrderId, r => DonationReceipt.FormatNumber(r.Series, r.Year, r.Number), ct);
            var methods = (await db.Payments.AsNoTracking().Where(p => ids.Contains(p.OrderId) && p.CapturedAt != null)
                    .OrderBy(p => p.CapturedAt).Select(p => new { p.OrderId, p.Method }).ToListAsync(ct))
                .GroupBy(p => p.OrderId).ToDictionary(g => g.Key, g => g.Last().Method);   // the last capture, as before
            foreach (var o in orders)
            {
                var lines = string.Join(" | ", o.Lines.OrderBy(l => l.LineNo).Select(l =>
                    $"{l.TitleAr}{(l.OptionLabelAr is { } opt ? $" ({opt})" : string.Empty)} × {l.Quantity.ToString(CultureInfo.InvariantCulture)} = {l.Amount.ToWire()}"));
                string[] cells =
                [
                    o.Reference,
                    clock.ToRiyadh(o.PaidAt!.Value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    o.Status.ToWire(),
                    o.DonorName,
                    SaudiMobile.Mask(o.DonorMobile),
                    lines,
                    o.TotalAmount.ToWire(),
                    o.RefundedAmount.ToWire(),
                    receipts.GetValueOrDefault(o.Id) ?? string.Empty,
                    methods.TryGetValue(o.Id, out var method) ? method.ToWire() : string.Empty,
                ];
                await writer.WriteAsync(string.Join(',', cells.Select(Cell)) + "\r\n");
            }

            await writer.FlushAsync(ct);
        }
    }

    /// RFC 4180 quoting + formula-injection guard.
    public static string Cell(string value)
    {
        var v = value ?? string.Empty;
        if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            v = "'" + v;
        }

        return v.AsSpan().IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{v.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : v;
    }
}
