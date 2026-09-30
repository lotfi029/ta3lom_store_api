using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Features.Settings;
using Ta3lom.Store.Domain.Entities.Documents;
using Ta3lom.Store.Domain.Entities.Media;
using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Domain.Services;

namespace Ta3lom.Store.Application.Features.Documents;

/// 09 §9.1: renders the invoice and donation receipt issued by settlement, stores each PDF privately at
/// `documents/{yyyy}/{number}.pdf` and attaches it. Documents are immutable: one with a PDF is never rendered again.
internal sealed class OrderDocuments(
    IApplicationDbContext db,
    IGenericRepository<Invoice> invoices,
    IGenericRepository<DonationReceipt> receipts,
    IGenericRepository<MediaAsset> media,
    IDocumentRenderer renderer,
    IFileStorage storage,
    ISellerProfile seller,
    IEInvoicingGateway eInvoicing,
    IStoreSettings settings,
    IUnitOfWork uow,
    IClock clock)
{
    private const string Currency = " ر.س";

    public async Task RenderForOrderAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId, ct)
                    ?? throw new InvalidOperationException($"Order {orderId} not found.");
        var s = await settings.GetAsync(ct);

        foreach (var invoice in await invoices.ListAsync(i => i.OrderId == orderId && i.Kind == InvoiceKind.Invoice && i.PdfAssetId == null, ct))
        {
            var qr = invoice.IsTaxInvoice && invoice.SellerVatNo is { } vatNo
                ? ZatcaQr.Encode(seller.NameAr, vatNo, invoice.IssuedAt, invoice.GrossTotal, invoice.VatTotal)
                : null;
            var bytes = renderer.RenderInvoice(InvoiceModel(order, invoice, s, qr));
            invoice.AttachPdf(await StoreAsync(invoice.Number, invoice.IssuedAt, bytes, ct), qr);
            await uow.SaveChangesAsync(ct);
            if (invoice.IsTaxInvoice)
            {
                await eInvoicing.ReportAsync(invoice.Id, ct);                               // Phase 2 (S2): no-op now
            }
        }

        foreach (var receipt in await receipts.ListAsync(r => r.OrderId == orderId && r.Kind == ReceiptKind.Receipt && r.PdfAssetId == null, ct))
        {
            var bytes = renderer.RenderDonationReceipt(await ReceiptModelAsync(order, receipt, ct));
            receipt.AttachPdf(await StoreAsync(receipt.Number, receipt.IssuedAt, bytes, ct));
            await uow.SaveChangesAsync(ct);
        }
    }

    private async Task<Guid> StoreAsync(string number, DateTimeOffset issuedAt, byte[] pdf, CancellationToken ct)
    {
        var key = FormattableString.Invariant($"documents/{clock.ToRiyadh(issuedAt).Year}/{number}.pdf");
        using (var stream = new MemoryStream(pdf, writable: false))
        {
            await storage.SaveAsync(key, isPublic: false, stream, ct);
        }

        var asset = MediaAsset.PrivateDocument(key, pdf.LongLength, clock.UtcNow);
        media.Add(asset);
        return asset.Id;
    }

    private static InvoiceDocument InvoiceModel(Order order, Invoice invoice, StoreSettings s, string? qr)
    {
        var lines = order.Lines.Where(l => l.Kind == LineKind.Buy).OrderBy(l => l.LineNo)
            .Select(l => new InvoiceDocumentLine(l.TitleAr, l.Quantity, l.UnitPrice.ToWire(), l.VatAmount.ToWire(), l.Amount.ToWire())).ToList();
        var shippingVat = invoice.IsTaxInvoice ? VatCalculator.Included(order.ShippingTotal, s.VatRate) : Money.Zero;
        return new InvoiceDocument(invoice.Number, invoice.IssuedAt, invoice.IsTaxInvoice, invoice.SellerVatNo, order.Reference, order.ContactName,
            order.ContactMobile, order.ContactEmail, lines, order.ShippingTotal.IsPositive ? order.ShippingTotal.ToWire() : null,
            order.ShippingTotal.IsPositive ? shippingVat.ToWire() : null, invoice.NetTotal.ToWire(), invoice.VatTotal.ToWire(),
            invoice.GrossTotal.ToWire() + Currency, invoice.IsTaxInvoice ? s.VatRate : 0m, qr);
    }

    private async Task<ReceiptDocument> ReceiptModelAsync(Order order, DonationReceipt receipt, CancellationToken ct)
    {
        var gifts = order.Lines.Where(l => l.Kind != LineKind.Buy).OrderBy(l => l.LineNo).ToList();
        var giftIds = gifts.Select(l => l.Id).ToList();
        var dedicatedTo = await db.Dedications.AsNoTracking().Where(d => giftIds.Contains(d.OrderLineId)).Select(d => d.ToName).ToListAsync(ct);
        var lines = gifts.Select(l => new ReceiptDocumentLine(l.TitleAr,
            l.Mode == LineMode.Units ? l.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture) : "مبلغ",
            l.Intent == GiftIntent.Zakat ? "زكاة" : "صدقة", l.Amount.ToWire())).ToList();
        return new ReceiptDocument(receipt.Number, receipt.IssuedAt, order.Reference, receipt.DonorName, order.IsAnonymous, lines,
            receipt.TotalAmount.ToWire() + Currency, receipt.ZakatAmount.ToWire(), dedicatedTo);
    }
}
