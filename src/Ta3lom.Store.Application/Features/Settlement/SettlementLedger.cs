using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Features.Settings;
using Ta3lom.Store.Domain.Entities.Documents;
using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Settlement;

/// The order-level steps after the line effects (09 §5.3, database/01 §4.4): goal counters and donor count (once per
/// order per product), zakat `in` (RUL-20), dedication cards (RUL-22) and document numbers (RUL-59).
internal sealed class SettlementLedger(
    IApplicationDbContext db,
    IGoalProgressRepository goals,
    IGenericRepository<ZakatLedgerEntry> zakat,
    IGenericRepository<Invoice> invoices,
    IGenericRepository<DonationReceipt> receipts,
    IDocumentNumberAllocator numbers,
    IClock clock)
{
    public async Task ApplyGiftsAsync(Order order, DateTimeOffset now, CancellationToken ct)
    {
        var gifts = order.Lines.Where(l => l.Kind != LineKind.Buy).ToList();
        foreach (var product in gifts.GroupBy(l => l.ProductId))
        {
            var raised = product.Select(l => l.Amount).Sum();
            var units = product.Where(l => l.Mode == LineMode.Units).Sum(l => l.Quantity);
            var zakatAmount = product.Where(l => l.Intent == GiftIntent.Zakat).Select(l => l.Amount).Sum();
            await goals.AddGiftAsync(product.Key, raised, units, donors: 1, zakatAmount, now, ct);
        }

        foreach (var line in gifts.Where(l => l.Intent == GiftIntent.Zakat))
        {
            zakat.Add(ZakatLedgerEntry.In(line.ProductId, line.Amount, line.Id, now));      // RUL-20
        }

        var giftLineIds = gifts.Select(l => l.Id).ToList();
        var dedications = await db.Dedications.AsNoTracking().Where(d => giftLineIds.Contains(d.OrderLineId)).Select(d => d.Id).ToListAsync(ct);
        foreach (var dedicationId in dedications)
        {
            order.RaiseEvent(new DedicationCard(dedicationId, order.Id));                  // RUL-22: only after payment
        }
    }

    public async Task IssueDocumentsAsync(Order order, StoreSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        var year = clock.RiyadhToday.Year;
        if (order.Lines.Any(l => l.Kind == LineKind.Buy))
        {
            var number = await numbers.NextAsync(DocumentSeries.Inv, year, ct);
            invoices.Add(Invoice.ForOrder(order.Id, number, settings.VatEnabled, settings.SellerVatNo, order.PurchasesTotal + order.ShippingTotal,
                order.VatTotal, now));
        }

        if (order.Lines.Any(l => l.Kind != LineKind.Buy))
        {
            var number = await numbers.NextAsync(DocumentSeries.Dr, year, ct);
            var zakatTotal = order.Lines.Where(l => l.Intent == GiftIntent.Zakat).Select(l => l.Amount).Sum();
            receipts.Add(DonationReceipt.ForOrder(order.Id, number, order.ContactName, order.GiftsTotal, zakatTotal, now));   // RUL-23: real name
        }
    }
}
