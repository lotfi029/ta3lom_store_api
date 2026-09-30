using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Features.Notifications;
using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Outbox.Handlers;

/// RUL-63: confirmation SMS to `contact_mobile` and receipt e-mail to `contact_email` (template `order_paid`), once each.
internal sealed class OrderPaidHandler(OrderMessages messages) : OutboxHandler<OrderPaid>
{
    protected override Task HandleAsync(OrderPaid message, OutboxEnvelope envelope, CancellationToken ct) =>
        messages.SendOrderPaidAsync(envelope, message.OrderId, ["sms", "email"], ct);
}

/// #88: the same message again on the requested channels (a new outbox id, so `ux_notification_once` allows it).
internal sealed class DocumentsResendRequestedHandler(OrderMessages messages) : OutboxHandler<DocumentsResendRequested>
{
    protected override Task HandleAsync(DocumentsResendRequested message, OutboxEnvelope envelope, CancellationToken ct) =>
        messages.SendOrderPaidAsync(envelope, message.OrderId, message.Channels, ct);
}

internal sealed class OrderMessages(IApplicationDbContext db, NotificationDispatch dispatch, IOrderStatusTokens tokens, IWebLinks links, IFileStorage storage)
{
    public async Task SendOrderPaidAsync(OutboxEnvelope envelope, Guid orderId, IReadOnlyList<string> channels, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct)
                    ?? throw new InvalidOperationException($"Order {orderId} not found.");
        var model = new
        {
            reference = order.Reference,
            total = order.GrandTotal.ToWire(),
            link = links.OrderThanks(order.Locale, order.Reference, tokens.Issue(order.Id)),
            name = order.ContactName,
        };
        if (channels.Contains("sms"))
        {
            await dispatch.SendOnceAsync(envelope, MessageChannel.Sms, order.ContactMobile, "order_paid", order.Locale, model, order.Id, ct);
        }

        if (channels.Contains("email"))
        {
            await dispatch.SendOnceAsync(envelope, MessageChannel.Email, order.ContactEmail, "order_paid", order.Locale, model, order.Id, ct,
                await AttachmentsAsync(order, ct));
        }
    }

    /// The rendered PDFs that are ready (a resend always has them; the first e-mail may go out before rendering).
    private async Task<IReadOnlyList<EmailAttachment>> AttachmentsAsync(Order order, CancellationToken ct)
    {
        var invoiceAssets = await db.Invoices.AsNoTracking().Where(i => i.OrderId == order.Id && i.PdfAssetId != null)
            .Select(i => new { i.Number, AssetId = i.PdfAssetId!.Value }).ToListAsync(ct);
        var receiptAssets = await db.DonationReceipts.AsNoTracking().Where(r => r.OrderId == order.Id && r.PdfAssetId != null)
            .Select(r => new { r.Number, AssetId = r.PdfAssetId!.Value }).ToListAsync(ct);
        var docs = invoiceAssets.Concat(receiptAssets).ToList();
        var assetIds = docs.Select(d => d.AssetId).ToList();
        var keys = await db.MediaAssets.AsNoTracking().Where(m => assetIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.StorageKey, ct);

        List<EmailAttachment> attachments = [];
        foreach (var doc in docs)
        {
            await using var stream = keys.TryGetValue(doc.AssetId, out var key) ? await storage.OpenReadAsync(key, isPublic: false, ct) : null;
            if (stream is null)
            {
                continue;
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            attachments.Add(new EmailAttachment($"{doc.Number}.pdf", "application/pdf", buffer.ToArray()));
        }

        return attachments;
    }
}
