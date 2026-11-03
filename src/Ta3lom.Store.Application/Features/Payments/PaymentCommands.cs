using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Donations;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Payments;

namespace Ta3lom.Store.Application.Features.Payments;

/// `POST /v1/orders/{reference}/pay?t=` (plan §3.3): the order's donor, or whoever holds its status token, opens a new
/// attempt — only for `pending_payment` before `paymentDueAt` (409 `payment.not_payable`). The previous open attempt is
/// cancelled at the gateway (best effort). Anything else is 404 (as the status route).
public sealed record PayOrderCommand(string Reference, string? Token, Guid? CustomerId) : ICommand<PayOrderResponse>;

internal sealed class PayOrderCommandHandler(IApplicationDbContext db, IOrderStatusTokens tokens, PaymentStarter starter)
    : ICommandHandler<PayOrderCommand, PayOrderResponse>
{
    public async Task<Result<PayOrderResponse>> HandleAsync(PayOrderCommand command, CancellationToken ct = default)
    {
        var reference = Order.NormalizeReference(command.Reference);
        var order = await db.Orders.AsNoTracking().Where(o => o.Reference == reference)
            .Select(o => new { o.Id, o.Reference, o.CustomerId, o.StatusTokenHash }).FirstOrDefaultAsync(ct);
        var allowed = order is not null
                      && ((command.CustomerId is { } customerId && order.CustomerId == customerId)
                          || (command.Token is { } token && tokens.Matches(token, order.StatusTokenHash)));
        if (!allowed)
        {
            return OrderErrors.NotFound;
        }

        var payment = await starter.StartAsync(order!.Id, newAttempt: true, ct);
        return payment.IsSuccess ? new PayOrderResponse(order.Reference, payment.Value) : payment.Error;
    }
}

/// `POST /v1/payments/webhooks/{provider}` (RUL-33/34, A-2): verify (constant time) → record in the inbox (a replay of a
/// processed event is a no-op) → RE-FETCH the payment from the gateway → settle. A bad signature is recorded with
/// `signature_valid = false` and answered 401. Valid but irrelevant events → 200 `ignored`. A gateway that cannot be
/// reached → 503 (the provider retries; the event stays unprocessed and is processed on the retry).
public sealed record PaymentWebhookCommand(string Provider, ReadOnlyMemory<byte> Body, IReadOnlyDictionary<string, string> Headers) : ICommand<string>;

internal sealed partial class PaymentWebhookCommandHandler(IPaymentGatewayRegistry gateways, IPaymentEventRepository events,
    PaymentSettlement settlement, IUnitOfWork uow, IClock clock, ILogger<PaymentWebhookCommandHandler> logger)
    : ICommandHandler<PaymentWebhookCommand, string>
{
    public const string Duplicate = "duplicate";
    public const string Ignored = "ignored";
    public const string Rejected = "rejected";

    /// The event types that can change a payment (Moyasar's names; `payment_faild` is a spelling in its docs).
    private static readonly HashSet<string> Handled = new(StringComparer.Ordinal)
    {
        "payment_paid", "payment_captured", "payment_failed", "payment_faild", "payment_voided", "payment_refunded", "payment_authorized",
    };

    public async Task<Result<string>> HandleAsync(PaymentWebhookCommand command, CancellationToken ct = default)
    {
        var gateway = gateways.Find(command.Provider);
        if (gateway is null)
        {
            return PaymentErrors.UnknownProvider;
        }

        var now = clock.UtcNow;
        var v = gateway.VerifyWebhook(command.Body.Span, command.Headers);
        if (!v.Valid)
        {
            // Own id space: an unverified body must never claim (and block) a real event id.
            var rejected = PaymentEvent.Receive(gateway.Name, $"invalid:{Guid.CreateVersion7():N}", v.Type ?? "unverified", null, false, v.RedactedPayload, now);
            rejected.Processed(null, Rejected, now);
            await events.InsertOrGetAsync(rejected, ct);   // one statement (ON CONFLICT DO NOTHING): its own implicit transaction
            LogRejected(logger, gateway.Name);
            return PaymentErrors.WebhookInvalid;
        }

        var (stored, inserted) = await events.InsertOrGetAsync(
            PaymentEvent.Receive(gateway.Name, v.EventId!, v.Type ?? "unknown", v.ProviderPaymentId, true, v.RedactedPayload, now), ct);
        if (!inserted && stored.ProcessedAt is not null)
        {
            return Duplicate;
        }

        string outcome;
        Guid? paymentId = null;
        if (v.Type is null || !Handled.Contains(v.Type) || (v.ProviderPaymentId is null && v.ProviderInvoiceId is null))
        {
            outcome = Ignored;
        }
        else
        {
            IReadOnlyList<GatewayPayment> fetched;
            try
            {
                fetched = v.ProviderPaymentId is { } id
                    ? (await gateway.GetPaymentAsync(id, ct)) is { } one ? [one] : []
                    : await gateway.FindPaymentsForHostedPageAsync(v.ProviderInvoiceId!, ct);
            }
            catch (PaymentGatewayException)
            {
                LogGatewayDown(logger, gateway.Name);
                return PaymentErrors.GatewayUnavailable;          // 503 → the provider retries; the event is still unprocessed
            }

            outcome = fetched.Count == 0 ? PaymentSettlement.UnknownPayment : Ignored;
            foreach (var payment in fetched)
            {
                (outcome, paymentId) = await settlement.ApplyAsync(gateway.Name, payment, ct);
            }
        }

        stored.Processed(paymentId, outcome, clock.UtcNow);       // the stored row is tracked
        await uow.SaveChangesAsync(ct);
        return outcome;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment webhook from {Provider} failed verification")]
    private static partial void LogRejected(ILogger logger, string provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment webhook from {Provider}: the gateway could not be reached for the re-fetch")]
    private static partial void LogGatewayDown(ILogger logger, string provider);
}
