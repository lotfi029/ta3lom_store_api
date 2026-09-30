namespace Ta3lom.Store.Domain.Entities.Payments;

/// Table `payment` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class Payment : AggregateRoot
{
    public Payment(Guid orderId, string provider, Money amount, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        OrderId = Guard.NotEmpty(orderId);
        Provider = Guard.NotNull(provider);
        Amount = amount;
        Status = PaymentStatus.Initiated;
        CreatedAt = now;
        UpdatedAt = now;
    }

    private Payment()
    {
    }

    public Guid OrderId { get; private set; }

    public string Provider { get; private set; } = null!;

    public string? ProviderPaymentId { get; private set; }

    public PaymentMethod? Method { get; private set; }

    public Money Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public string? FailureCode { get; private set; }

    public string? FailureMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// A capture reported by a provider (doc 15 §4: the payment service owns this table). In this backend only the
    /// Development trigger records one, so the integrity check "paid orders have a captured payment" holds.
    public static Payment Captured(Guid orderId, string provider, string providerPaymentId, PaymentMethod? method, Money amount, DateTimeOffset now) =>
        new(orderId, provider, amount, now) { ProviderPaymentId = providerPaymentId, Method = method, Status = PaymentStatus.Captured };
}
