using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Features.Notifications;
using Ta3lom.Store.Application.Features.Receipts;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Outbox.Handlers;

/// RUL-29/35: a captured payment needs finance — the staff alert e-mail (`staff_alert` template), once per message.
/// Reference, reason and amount only: no donor data.
internal sealed class PaymentReviewRequiredHandler(NotificationDispatch dispatch, IEmailSender email) : OutboxHandler<PaymentReviewRequired>
{
    public const string Kind = "payment_review";

    protected override Task HandleAsync(PaymentReviewRequired message, OutboxEnvelope envelope, CancellationToken ct) =>
        dispatch.SendOnceAsync(envelope, MessageChannel.Email, email.StaffAlertsAddress, "staff_alert", "ar",
            new
            {
                subject = string.Create(CultureInfo.InvariantCulture, $"{Kind} {message.Reference}"),
                body = string.Create(CultureInfo.InvariantCulture,
                    $"kind: {Kind}\nreference: {message.Reference}\nreason: {message.Reason}\ncaptured: {message.CapturedAmount} SAR\nat: {envelope.OccurredAt:O}"),
            },
            ct,
            subject: string.Create(CultureInfo.InvariantCulture, $"kind {Kind}, reference {message.Reference}"));
}

/// An unpaid order expired: logged only (the cart was emptied at checkout; nothing is restored).
internal sealed partial class OrderExpiredHandler(ILogger<OrderExpiredHandler> logger) : OutboxHandler<OrderExpired>
{
    protected override Task HandleAsync(OrderExpired message, OutboxEnvelope envelope, CancellationToken ct)
    {
        LogExpired(logger, message.Reference);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {Reference} expired unpaid")]
    private static partial void LogExpired(ILogger logger, string reference);
}

/// A settled order (plan §4): the donor gets an SMS (template `order_paid`, link to the order page with its status token)
/// and, when the order has an e-mail, the same with the DR receipt PDF attached. Each once per channel and recipient
/// (`notification.dedupe_key`). An anonymised donor gets nothing.
internal sealed class OrderPaidHandler(IApplicationDbContext db, NotificationDispatch dispatch, ReceiptFiles receipts,
    IOrderStatusTokens tokens, IWebLinks links) : OutboxHandler<OrderPaid>
{
    protected override async Task HandleAsync(OrderPaid message, OutboxEnvelope envelope, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Where(o => o.Id == message.OrderId)
            .Select(o => new { o.Reference, o.DonorMobile, o.DonorEmail, o.Locale, o.TotalAmount }).FirstAsync(ct);
        var file = await receipts.GetOrRenderAsync(message.ReceiptId, ct);
        var number = Path.GetFileNameWithoutExtension(file.FileName);
        var model = new
        {
            reference = order.Reference,
            amount = order.TotalAmount.ToWire(),
            receiptNumber = number,
            link = links.OrderStatus(order.Locale, order.Reference, tokens.Create(order.Reference).Token),
        };
        await dispatch.SendOnceAsync(envelope, MessageChannel.Sms, order.DonorMobile, "order_paid", order.Locale, model, ct,
            subject: $"order_paid {order.Reference}");
        if (order.DonorEmail != Domain.Entities.Donations.Order.AnonymisedEmail)
        {
            await dispatch.SendOnceAsync(envelope, MessageChannel.Email, order.DonorEmail, "order_paid", order.Locale, model, ct,
                subject: $"order_paid {order.Reference}", attachments: [new EmailAttachment(file.FileName, "application/pdf", file.Pdf)]);
        }
    }
}

/// RenderReceipt (plan §4): a receipt was issued — render its PDF once into private storage (idempotent).
internal sealed class ReceiptIssuedHandler(ReceiptFiles receipts) : OutboxHandler<ReceiptIssued>
{
    protected override Task HandleAsync(ReceiptIssued message, OutboxEnvelope envelope, CancellationToken ct) =>
        receipts.GetOrRenderAsync(message.ReceiptId, ct);
}

/// A succeeded refund (plan §4): SMS `refund_processed` and, with an e-mail, the same e-mail — with the DRC PDF attached
/// when the order was fully refunded. Once per channel and recipient.
internal sealed class RefundProcessedHandler(IApplicationDbContext db, NotificationDispatch dispatch, ReceiptFiles receipts,
    IOrderStatusTokens tokens, IWebLinks links) : OutboxHandler<RefundProcessed>
{
    protected override async Task HandleAsync(RefundProcessed message, OutboxEnvelope envelope, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Where(o => o.Id == message.OrderId)
            .Select(o => new { o.Reference, o.DonorMobile, o.DonorEmail, o.Locale }).FirstAsync(ct);
        var file = message.ReceiptId is { } drc ? await receipts.GetOrRenderAsync(drc, ct) : null;
        var model = new
        {
            reference = order.Reference,
            amount = message.Amount,
            receiptNumber = file is null ? null : Path.GetFileNameWithoutExtension(file.FileName),
            link = links.OrderStatus(order.Locale, order.Reference, tokens.Create(order.Reference).Token),
        };
        await dispatch.SendOnceAsync(envelope, MessageChannel.Sms, order.DonorMobile, "refund_processed", order.Locale, model, ct,
            subject: $"refund_processed {order.Reference}");
        if (order.DonorEmail != Domain.Entities.Donations.Order.AnonymisedEmail)
        {
            await dispatch.SendOnceAsync(envelope, MessageChannel.Email, order.DonorEmail, "refund_processed", order.Locale, model, ct,
                subject: $"refund_processed {order.Reference}",
                attachments: file is null ? null : [new EmailAttachment(file.FileName, "application/pdf", file.Pdf)]);
        }
    }
}
