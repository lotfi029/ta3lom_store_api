using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Orders;
using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Application.Features.Orders;

/// Read models of an order (11 §5.10 `OrderDetail`, §9.9 `AdminOrderDetail`), shared by admin and `/v1/me` (S-21, S-31).
internal sealed class OrderReader(IApplicationDbContext db)
{
    public async Task<Order?> FindAsync(string reference, CancellationToken ct) =>
        await db.Orders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Reference == reference, ct);

    public async Task<OrderDetailResponse> DetailAsync(Order order, CancellationToken ct)
    {
        var parts = await PartsAsync(order, ct);
        return new OrderDetailResponse(order.Reference, order.Status.ToWire(), order.PlacedAt, order.PaidAt, order.PurchasesTotal.ToWire(),
            order.ShippingTotal.ToWire(), order.GiftsTotal.ToWire(), order.VatTotal.ToWire(), order.GrandTotal.ToWire(), order.RefundedTotal.ToWire(),
            order.IsAnonymous, parts.Lines, [.. parts.Payments.Select(p => new OrderPaymentItem(p.Method?.ToWire(), p.Status.ToWire(), p.Amount.ToWire(), p.CreatedAt))],
            parts.Documents, parts.Shipment);
    }

    public async Task<AdminOrderDetailResponse> AdminDetailAsync(Order order, CancellationToken ct)
    {
        var parts = await PartsAsync(order, ct);
        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == order.CustomerId)
            .Select(c => new AdminOrderCustomer(c.Id, c.Mobile, c.FullName)).FirstAsync(ct);
        var notifications = await db.Notifications.AsNoTracking().Where(n => n.RelatedOrderId == order.Id).OrderBy(n => n.CreatedAt).ToListAsync(ct);
        var refunds = await db.Refunds.AsNoTracking().Where(r => r.OrderId == order.Id).OrderBy(r => r.CreatedAt).ToListAsync(ct);
        var a = order.ShippingAddress;
        return new AdminOrderDetailResponse(order.Reference, order.Status.ToWire(), order.PlacedAt, order.PaidAt, order.PaymentDueAt, order.CancelledAt,
            order.PurchasesTotal.ToWire(), order.ShippingTotal.ToWire(), order.GiftsTotal.ToWire(), order.VatTotal.ToWire(), order.GrandTotal.ToWire(),
            order.RefundedTotal.ToWire(), order.IsAnonymous, parts.Lines, parts.Documents, parts.Shipment, customer,
            new AdminOrderContact(order.ContactName, order.ContactMobile, order.ContactEmail),
            a is null ? null : new AdminShippingAddress(a.RecipientName, a.RecipientMobile, a.City, a.District, a.ShortAddress, a.Street, a.BuildingNo, a.PostalCode),
            [.. parts.Payments.Select(p => new AdminOrderPayment(p.Id, p.Provider, p.ProviderPaymentId, p.Method?.ToWire(), p.Amount.ToWire(), p.Status.ToWire(),
                p.FailureCode, p.CreatedAt))],
            [],                                                                               // payment_event rows belong to the payment service (doc 15)
            [.. notifications.Select(n => new AdminOrderNotification(n.Channel.ToWire(), n.TemplateKey,
                n.Channel == MessageChannel.Sms ? SaudiMobile.Mask(n.Recipient) : MaskEmail(n.Recipient), n.Status.ToWire(), n.SentAt))],
            [.. refunds.Select(r => new AdminOrderRefund(r.Id, r.Amount.ToWire(), r.Status.ToWire(), r.Reason, r.CreatedAt, r.CompletedAt))],
            order.SourceCartId);
    }

    private async Task<Parts> PartsAsync(Order order, CancellationToken ct)
    {
        var lineIds = order.Lines.Select(l => l.Id).ToList();
        var formatIds = order.Lines.Where(l => l.BookFormatId is not null).Select(l => l.BookFormatId!.Value).ToList();
        var kinds = await db.BookFormats.AsNoTracking().Where(f => formatIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Kind, ct);
        var dedications = await db.Dedications.AsNoTracking().Where(d => lineIds.Contains(d.OrderLineId)).ToDictionaryAsync(d => d.OrderLineId, ct);
        var lines = order.Lines.OrderBy(l => l.LineNo).Select(l => new OrderLineResponse(l.LineNo, l.Kind.ToWire(), l.Mode.ToWire(), l.TitleAr, l.TitleEn,
            l.BookFormatId is { } f && kinds.TryGetValue(f, out var kind) ? kind.ToWire() : null, l.Quantity, l.UnitPrice.ToWire(), l.Amount.ToWire(),
            l.VatAmount.ToWire(), l.Intent?.ToWire(), l.FulfilmentStatus.ToWire(), l.RefundedAmount.ToWire(),
            dedications.TryGetValue(l.Id, out var d) ? new OrderLineDedication(d.ToName, d.SmsStatus.ToWire()) : null)).ToList();

        var payments = await db.Payments.AsNoTracking().Where(p => p.OrderId == order.Id).OrderBy(p => p.CreatedAt).ToListAsync(ct);
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.OrderId == order.Id)
            .Select(i => new OrderDocumentItem(i.Kind == InvoiceKind.Invoice ? "invoice" : "credit_note", i.Number, i.IssuedAt, i.PdfAssetId != null)).ToListAsync(ct);
        var receipts = await db.DonationReceipts.AsNoTracking().Where(r => r.OrderId == order.Id)
            .Select(r => new OrderDocumentItem(r.Kind == ReceiptKind.Receipt ? "receipt" : "cancellation", r.Number, r.IssuedAt, r.PdfAssetId != null)).ToListAsync(ct);
        var shipment = await db.Shipments.AsNoTracking().Where(s => s.OrderId == order.Id)
            .Select(s => new { s.Status, s.Carrier, s.TrackingNo }).FirstOrDefaultAsync(ct);
        return new Parts(lines, payments, [.. invoices, .. receipts],
            shipment is null ? null : new OrderShipmentInfo(shipment.Status.ToWire(), shipment.Carrier, shipment.TrackingNo));
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? "•••" : string.Concat(email.AsSpan(0, 1), "•••", email.AsSpan(at));
    }

    private sealed record Parts(List<OrderLineResponse> Lines, List<Domain.Entities.Payments.Payment> Payments, List<OrderDocumentItem> Documents,
        OrderShipmentInfo? Shipment);
}
