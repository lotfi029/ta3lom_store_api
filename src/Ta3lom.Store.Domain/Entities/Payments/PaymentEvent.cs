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

    public void Processed(Guid? paymentId, string outcome, DateTimeOffset now)
    {
        PaymentId ??= paymentId;
        Outcome = outcome;
        ProcessedAt = now;
    }
}
