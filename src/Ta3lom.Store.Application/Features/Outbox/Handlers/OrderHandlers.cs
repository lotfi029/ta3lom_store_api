using Microsoft.Extensions.Logging;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Outbox.Handlers;

/// A new donation order: logged only (reference and amount, no donor data) until R5-Payments adds the donor and staff
/// messages (owner decision D-a). The message type stays so those handlers can be added without a schema change.
internal sealed partial class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger) : OutboxHandler<OrderPlaced>
{
    protected override Task HandleAsync(OrderPlaced message, OutboxEnvelope envelope, CancellationToken ct)
    {
        LogPlaced(logger, message.Reference, message.TotalAmount, message.LineCount);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {Reference} placed: {TotalAmount} SAR in {LineCount} lines")]
    private static partial void LogPlaced(ILogger logger, string reference, string totalAmount, int lineCount);
}

/// Staff cancelled a pending order: logged only (D-a).
internal sealed partial class OrderCancelledHandler(ILogger<OrderCancelledHandler> logger) : OutboxHandler<OrderCancelled>
{
    protected override Task HandleAsync(OrderCancelled message, OutboxEnvelope envelope, CancellationToken ct)
    {
        LogCancelled(logger, message.Reference);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {Reference} cancelled")]
    private static partial void LogCancelled(ILogger logger, string reference);
}
