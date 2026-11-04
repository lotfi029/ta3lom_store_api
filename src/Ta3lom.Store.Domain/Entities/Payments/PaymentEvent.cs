namespace Ta3lom.Store.Domain.Entities.Payments;

/// Table `payment_event` (RUL-34): the webhook inbox, append-only, `UNIQUE (provider, provider_event_id)` so a replayed
/// event is a no-op. The payload is kept redacted (the gateway sends no card number; the secret token is removed).
public sealed class PaymentEvent : Entity
{
    private PaymentEvent(Guid id) : base(id)
    {
    }

    private PaymentEvent()
    {
    }

    public string Provider { get; private set; } = null!;

    public string ProviderEventId { get; private set; } = null!;

    public string Type { get; private set; } = null!;

    public Guid? PaymentId { get; private set; }

    public string? ProviderPaymentId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public bool SignatureValid { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    /// What the event did: `settled`, `review`, `failed`, `ignored`, `duplicate`, `rejected`, …
    public string? Outcome { get; private set; }

    public string PayloadJson { get; private set; } = "{}";

    /// An orphan (review 04 A-3): the captured payment the gateway reported, for which we have no attempt.
    public string? ProviderInvoiceId { get; private set; }

    public Money? GatewayAmount { get; private set; }

    public string? GatewayCurrency { get; private set; }

    /// Inbox claim (S-5): one delivery processes the event; a claim older than <see cref="ClaimTimeout"/> may be retaken.
    public DateTimeOffset? ClaimedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public string? ResolvedBy { get; private set; }

    public string? ResolutionNote { get; private set; }

    public const string OrphanOutcome = "orphan";

    public static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(2);

    public static PaymentEvent Receive(string provider, string providerEventId, string type, string? providerPaymentId,
        bool signatureValid, string payloadJson, DateTimeOffset now) =>
        new(Guid.CreateVersion7())
        {
            Provider = Guard.NotEmpty(provider),
            ProviderEventId = Guard.NotEmpty(providerEventId),
            Type = string.IsNullOrWhiteSpace(type) ? "unknown" : type.Length > 64 ? type[..64] : type,
            ProviderPaymentId = providerPaymentId,
            SignatureValid = signatureValid,
            PayloadJson = payloadJson,
            ReceivedAt = now,
        };

    /// A captured payment with no attempt of ours (review 04 A-3): kept with its amount for finance.
    public void MarkOrphan(string? providerPaymentId, string? providerInvoiceId, Money amount, string currency, DateTimeOffset now)
    {
        ProviderPaymentId ??= providerPaymentId;
        ProviderInvoiceId = providerInvoiceId;
        GatewayAmount = amount;
        GatewayCurrency = string.IsNullOrWhiteSpace(currency) ? null : currency.Length > 8 ? currency[..8] : currency;
        Outcome = OrphanOutcome;
        ProcessedAt = now;
    }

    /// Finance looked into an orphan payment (refunded it on the dashboard, matched it, …) and records how.
    public Result Resolve(string resolvedBy, string note, DateTimeOffset now)
    {
        if (Outcome != OrphanOutcome)
        {
            return PaymentErrors.OrphanNotFound;
        }

        if (ResolvedAt is not null)
        {
            return PaymentErrors.OrphanResolved;
        }

        var trimmed = Guard.NotEmpty(note).Trim();
        ResolvedBy = Guard.NotEmpty(resolvedBy);
        ResolutionNote = trimmed.Length > 500 ? trimmed[..500] : trimmed;
        ResolvedAt = now;
        return Result.Success();
    }

    public void Processed(Guid? paymentId, string outcome, DateTimeOffset now)
    {
        PaymentId ??= paymentId;
        Outcome = outcome;
        ProcessedAt = now;
    }
}
