using Microsoft.Extensions.Logging;
using Ta3lom.Store.Application.Features.Notifications;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Outbox.Handlers;

/// A new donation order: logged (reference and amount only, no donor data) and one staff alert. The donor's payment
/// messages arrive with R5-Payments.
internal sealed partial class OrderPlacedHandler(NotificationDispatch dispatch, IEmailSender email, ILogger<OrderPlacedHandler> logger)
    : OutboxHandler<OrderPlaced>
{
    protected override Task HandleAsync(OrderPlaced message, OutboxEnvelope envelope, CancellationToken ct)
    {
        LogPlaced(logger, message.Reference, message.TotalAmount, message.LineCount);
        return dispatch.SendOnceAsync(envelope, MessageChannel.Email, email.StaffAlertsAddress, "staff_alert", "ar",
            new { subject = $"order_placed {message.Reference}", body = $"reference: {message.Reference}\ntotal: {message.TotalAmount}\nlines: {message.LineCount}" },
            ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {Reference} placed: {TotalAmount} SAR in {LineCount} lines")]
    private static partial void LogPlaced(ILogger logger, string reference, string totalAmount, int lineCount);
}

/// Staff cancelled a pending order: logged and one staff alert.
internal sealed partial class OrderCancelledHandler(NotificationDispatch dispatch, IEmailSender email, ILogger<OrderCancelledHandler> logger)
    : OutboxHandler<OrderCancelled>
{
    protected override Task HandleAsync(OrderCancelled message, OutboxEnvelope envelope, CancellationToken ct)
    {
        LogCancelled(logger, message.Reference);
        return dispatch.SendOnceAsync(envelope, MessageChannel.Email, email.StaffAlertsAddress, "staff_alert", "ar",
            new { subject = $"order_cancelled {message.Reference}", body = $"reference: {message.Reference}\ncancelled by: {message.CancelledBy}" },
            ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {Reference} cancelled")]
    private static partial void LogCancelled(ILogger logger, string reference);
}
