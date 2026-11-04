namespace Ta3lom.Store.Application.Interfaces;

/// Port to a hosted-payment gateway (ADR-S05, F-D1). Amounts are integer halalas. Card data never passes through us:
/// the donor pays on the gateway's page; the webhook (or reconciliation) is the only proof of payment, and the payment
/// is always re-fetched from the gateway before an order is settled. One adapter per provider in
/// `Infrastructure/Payments/{Provider}`; `Name` is stored on every `payment` row.
public interface IPaymentGateway
{
    /// "moyasar" | "fake".
    string Name { get; }

    /// Creates the hosted page for one attempt. Never throws for a gateway refusal or a transport failure: `Ok = false`.
    Task<HostedPaymentResult> CreateHostedPaymentAsync(HostedPaymentRequest request, CancellationToken ct = default);

    /// The payment as the gateway sees it now; null when it does not exist. Throws <see cref="PaymentGatewayException"/>
    /// when the gateway cannot be reached (the caller retries later: webhook retry or reconciliation).
    Task<GatewayPayment?> GetPaymentAsync(string providerPaymentId, CancellationToken ct = default);

    /// Every payment made on a hosted page (reconciliation when only the hosted-page id is known). Throws like
    /// <see cref="GetPaymentAsync"/>.
    Task<IReadOnlyList<GatewayPayment>> FindPaymentsForHostedPageAsync(string providerInvoiceId, CancellationToken ct = default);

    /// Refunds `amountHalalas` of a captured payment. Never throws for a refusal or a transport failure: `Ok = false`.
    Task<GatewayRefundResult> RefundAsync(string providerPaymentId, long amountHalalas, string idempotencyKey, CancellationToken ct = default);

    /// Best effort: the hosted page can no longer be paid (a newer attempt replaced it). Never throws.
    Task CancelHostedPaymentAsync(string providerInvoiceId, CancellationToken ct = default);

    /// Authenticates a webhook in constant time and extracts its identifiers. Never trusted for amounts or status.
    WebhookVerification VerifyWebhook(ReadOnlySpan<byte> rawBody, IReadOnlyDictionary<string, string> headers);
}

/// Resolves gateways by name (webhook route `/v1/payments/webhooks/{provider}`) and the configured default for new payments.
public interface IPaymentGatewayRegistry
{
    IPaymentGateway Default { get; }

    IPaymentGateway? Find(string name);
}

/// `ReturnUrl` is the web order page (`/{locale}/orders/{ref}?t=`): where the donor comes back — never proof of payment.
public sealed record HostedPaymentRequest(Guid PaymentId, string OrderReference, long AmountHalalas, string Description,
    string ReturnUrl, DateTimeOffset ExpiresAt);

public sealed record HostedPaymentResult(bool Ok, string? ProviderInvoiceId, string? RedirectUrl, DateTimeOffset? ExpiresAt, string? Error)
{
    public static HostedPaymentResult Created(string invoiceId, string redirectUrl, DateTimeOffset? expiresAt) => new(true, invoiceId, redirectUrl, expiresAt, null);

    public static HostedPaymentResult Failed(string error) => new(false, null, null, null, error);
}

public enum GatewayPaymentStatus
{
    Initiated,
    Authorized,
    Captured,
    Failed,
    Voided,
    Refunded,
}

/// A payment re-fetched from the gateway. `CapturedHalalas` is what was really taken; `RefundedHalalas` what went back.
public sealed record GatewayPayment(string ProviderPaymentId, string? ProviderInvoiceId, GatewayPaymentStatus Status,
    long AmountHalalas, long CapturedHalalas, long RefundedHalalas, string? Currency, PaymentMethod Method,
    string? FailureCode, string? FailureMessage, bool Live);

/// `Pending` (review 04 P-3): no definite answer — a timeout, a 5xx, a dropped connection. The refund may or may not have
/// happened at the gateway: it stays `requested` until the refund reconciliation reads the payment's refunded amount.
public sealed record GatewayRefundResult(bool Ok, string? ProviderRefundId, string? Error, bool Pending = false)
{
    public static GatewayRefundResult Succeeded(string? id) => new(true, id, null);

    /// A definite refusal (4xx): nothing was refunded.
    public static GatewayRefundResult Failed(string error) => new(false, null, error);

    public static GatewayRefundResult Unknown(string error) => new(false, null, error, true);
}

/// `Valid = false` → 401 and the event recorded with `signature_valid = false`. `RedactedPayload` never holds the secret.
public sealed record WebhookVerification(bool Valid, string? EventId, string? Type, string? ProviderPaymentId,
    string? ProviderInvoiceId, string RedactedPayload)
{
    public static WebhookVerification Invalid(string? eventId, string redacted) => new(false, eventId, null, null, null, redacted);
}

/// The gateway could not be reached or answered 5xx (the caller retries later).
public sealed class PaymentGatewayException : Exception
{
    public PaymentGatewayException()
    {
    }

    public PaymentGatewayException(string message)
        : base(message)
    {
    }

    public PaymentGatewayException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
