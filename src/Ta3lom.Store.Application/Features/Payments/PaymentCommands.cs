using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Donations;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Payments;
using Ta3lom.Store.Domain.Events;

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

/// `POST /v1/payments/webhooks/{provider}` (RUL-33/34, A-2, review 04): verify (constant time) → record in the inbox (a
/// replay of a processed event is a no-op) → claim it (S-5: one delivery processes an event; a claim older than
/// <see cref="PaymentEvent.ClaimTimeout"/> is taken over) → RE-FETCH the payment from the gateway (S-4: 5 s, twice) →
/// settle. A captured payment we have no attempt for is an orphan (A-3): recorded on the event with its amount and
/// currency for finance, and a staff alert. A bad signature is recorded with `signature_valid = false` and fixed-size
/// metadata only (S-3: length, SHA-256, type — never the body) and answered 401. Valid but irrelevant events → 200
/// `ignored`. A gateway that cannot be reached → 503 (the provider retries; the claim is released).
public sealed record PaymentWebhookCommand(string Provider, ReadOnlyMemory<byte> Body, IReadOnlyDictionary<string, string> Headers) : ICommand<string>;

internal sealed partial class PaymentWebhookCommandHandler(IPaymentGatewayRegistry gateways, IPaymentEventRepository events,
    PaymentSettlement settlement, IOutbox outbox, IUnitOfWork uow, IClock clock, ILogger<PaymentWebhookCommandHandler> logger)
    : ICommandHandler<PaymentWebhookCommand, string>
{
    public const string Duplicate = "duplicate";
    public const string Ignored = "ignored";
    public const string Rejected = "rejected";

    /// S-4: the provider waits for our answer — each re-fetch gets 5 s, and one more try.
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(5);
    public const int FetchTries = 2;

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
            // Own id space: an unverified body must never claim (and block) a real event id. S-3: fixed-size metadata only.
            var type = v.Type is { Length: <= 64 } t && t.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.') ? t : null;
            var metadata = JsonSerializer.Serialize(new RejectedBody(command.Body.Length, Convert.ToHexStringLower(SHA256.HashData(command.Body.Span)), type), WebJson);
            var rejected = PaymentEvent.Receive(gateway.Name, $"invalid:{Guid.CreateVersion7():N}", type ?? "unverified", null, false, metadata, now);
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

        if (!await events.TryClaimAsync(stored.Id, now, now - PaymentEvent.ClaimTimeout, ct))
        {
            return Duplicate;                                     // another delivery of this event is being processed
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
                fetched = await FetchAsync(gateway, v, ct);
            }
            catch (PaymentGatewayException)
            {
                await events.ReleaseClaimAsync(stored.Id, CancellationToken.None);
                LogGatewayDown(logger, gateway.Name);
                return PaymentErrors.GatewayUnavailable;          // 503 → the provider retries; the event is still unprocessed
            }

            outcome = fetched.Count == 0 ? PaymentSettlement.UnknownPayment : Ignored;
            foreach (var payment in fetched)
            {
                (outcome, paymentId) = await settlement.ApplyAsync(gateway.Name, payment, ct);
                if (outcome == PaymentSettlement.UnknownPayment && payment.CapturedHalalas > 0)
                {
                    stored.MarkOrphan(payment.ProviderPaymentId, payment.ProviderInvoiceId, Money.FromHalalas(payment.CapturedHalalas),
                        payment.Currency ?? string.Empty, clock.UtcNow);
                    outbox.Enqueue(new StaffAlert("orphan_payment", null,
                        $"{gateway.Name} payment {payment.ProviderPaymentId}: {Money.FromHalalas(payment.CapturedHalalas).ToWire()} {payment.Currency ?? "?"} with no order"));
                    LogOrphan(logger, gateway.Name);
                    outcome = PaymentEvent.OrphanOutcome;
                }
            }
        }

        if (outcome != PaymentEvent.OrphanOutcome)
        {
            stored.Processed(paymentId, outcome, clock.UtcNow);   // the stored row is tracked
        }

        await uow.SaveChangesAsync(ct);
        return outcome;
    }

    private static async Task<IReadOnlyList<GatewayPayment>> FetchAsync(IPaymentGateway gateway, WebhookVerification v, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(FetchTimeout);
            try
            {
                return v.ProviderPaymentId is { } id
                    ? (await gateway.GetPaymentAsync(id, budget.Token)) is { } one ? [one] : []
                    : await gateway.FindPaymentsForHostedPageAsync(v.ProviderInvoiceId!, budget.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (attempt >= FetchTries)
                {
                    throw new PaymentGatewayException("the gateway did not answer in time");
                }
            }
            catch (PaymentGatewayException) when (attempt < FetchTries)
            {
            }
        }
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private sealed record RejectedBody(int Length, string Sha256, string? Type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment webhook from {Provider} failed verification")]
    private static partial void LogRejected(ILogger logger, string provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment webhook from {Provider}: the gateway could not be reached for the re-fetch")]
    private static partial void LogGatewayDown(ILogger logger, string provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment webhook from {Provider}: a captured payment matches no attempt (orphan)")]
    private static partial void LogOrphan(ILogger logger, string provider);
}
