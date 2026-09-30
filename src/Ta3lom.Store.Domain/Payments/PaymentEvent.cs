using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Domain.Payments;

/// Table `payment_event` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class PaymentEvent
{
    public PaymentEvent(string provider, string providerEventId, string eventType, bool signatureValid, string payload, DateTimeOffset now)
    {
        Provider = Guard.NotNull(provider);
        ProviderEventId = Guard.NotNull(providerEventId);
        EventType = Guard.NotNull(eventType);
        SignatureValid = signatureValid;
        Payload = Guard.NotNull(payload);
        ReceivedAt = now;
    }

    private PaymentEvent()
    {
    }

    public long Id { get; private set; }

    public string Provider { get; private set; } = null!;

    public string ProviderEventId { get; private set; } = null!;

    public string EventType { get; private set; } = null!;

    public bool SignatureValid { get; private set; }

    public string Payload { get; private set; } = null!;

    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? Error { get; private set; }
}
