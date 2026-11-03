using System.Globalization;
using Microsoft.Extensions.Logging;
using Ta3lom.Store.Application.Features.Notifications;
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

/// A settled order (reference and amount only). The donor SMS/e-mail with the receipt is sent by F3's handler.
internal sealed partial class OrderPaidHandler(ILogger<OrderPaidHandler> logger) : OutboxHandler<OrderPaid>
{
    protected override Task HandleAsync(OrderPaid message, OutboxEnvelope envelope, CancellationToken ct)
    {
        LogPaid(logger, message.Reference, message.TotalAmount);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {Reference} paid: {TotalAmount} SAR")]
    private static partial void LogPaid(ILogger logger, string reference, string totalAmount);
}

/// A receipt was issued (its PDF is rendered by F3's handler).
internal sealed partial class ReceiptIssuedHandler(ILogger<ReceiptIssuedHandler> logger) : OutboxHandler<ReceiptIssued>
{
    protected override Task HandleAsync(ReceiptIssued message, OutboxEnvelope envelope, CancellationToken ct)
    {
        LogIssued(logger, message.Number);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Receipt {Number} issued")]
    private static partial void LogIssued(ILogger logger, string number);
}
