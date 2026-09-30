namespace Ta3lom.Store.Domain.Entities.Payments;

/// Table `refund` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class Refund : AggregateRoot
{
    public Refund(Guid orderId, Guid paymentId, Money amount, string reason, string requestedBy, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        OrderId = Guard.NotEmpty(orderId);
        PaymentId = Guard.NotEmpty(paymentId);
        Amount = amount;
        Reason = Guard.NotNull(reason);
        Status = RefundStatus.Requested;
        RequestedBy = Guard.NotNull(requestedBy);
        CreatedAt = now;
    }

    private Refund()
    {
    }

    public Guid OrderId { get; private set; }

    public Guid PaymentId { get; private set; }

    public Money Amount { get; private set; }

    public string Reason { get; private set; } = null!;

    public RefundStatus Status { get; private set; }

    public string? ProviderRefundId { get; private set; }

    public string RequestedBy { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }
}
