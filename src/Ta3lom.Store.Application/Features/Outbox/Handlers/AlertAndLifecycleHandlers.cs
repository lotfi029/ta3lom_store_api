using System.Globalization;
using Ta3lom.Store.Application.Features.Notifications;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Outbox.Handlers;

/// Staff e-mail `staff_alert` (payment review, duplicate course, effect missing …), once per message.
internal sealed class FinanceAlertHandler(NotificationDispatch dispatch, IEmailSender email) : OutboxHandler<FinanceAlert>
{
    protected override Task HandleAsync(FinanceAlert message, OutboxEnvelope envelope, CancellationToken ct) =>
        dispatch.SendOnceAsync(envelope, MessageChannel.Email, email.StaffAlertsAddress, "staff_alert", "ar",
            new
            {
                subject = string.Create(CultureInfo.InvariantCulture, $"FinanceAlert {message.Kind} {message.Reference}"),
                body = string.Create(CultureInfo.InvariantCulture,
                    $"kind: {message.Kind}\norder: {message.Reference ?? message.OrderId?.ToString() ?? "-"}\ndetail: {message.Detail}\nat: {envelope.OccurredAt:O}"),
            },
            message.OrderId, ct);
}

/// Metrics-only messages (09 §4.3): nothing is sent to the customer.
internal sealed class OrderPlacedHandler : OutboxHandler<OrderPlaced>
{
    protected override Task HandleAsync(OrderPlaced message, OutboxEnvelope envelope, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class OrderExpiredHandler : OutboxHandler<OrderExpired>
{
    protected override Task HandleAsync(OrderExpired message, OutboxEnvelope envelope, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class OrderCancelledHandler : OutboxHandler<OrderCancelled>
{
    protected override Task HandleAsync(OrderCancelled message, OutboxEnvelope envelope, CancellationToken ct) => Task.CompletedTask;
}

/// RUL-59: renders the order's documents after commit (idempotent: a document with a PDF is skipped).
internal sealed class RenderDocumentsHandler(Documents.OrderDocuments documents) : OutboxHandler<RenderDocuments>
{
    protected override Task HandleAsync(RenderDocuments message, OutboxEnvelope envelope, CancellationToken ct) =>
        documents.RenderForOrderAsync(message.OrderId, ct);
}
