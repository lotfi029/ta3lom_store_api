namespace Ta3lom.Store.Domain.Entities.Payments;

/// Table `refund` (F-D5): a finance refund of an amount of one captured payment. Requested before the gateway call
/// (outside any transaction), then succeeded or failed. Idempotent on (order, `Idempotency-Key`).
public sealed class Refund : Entity
{
    private Refund(Guid id) : base(id)
    {
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

    public string? FailureMessage { get; private set; }

    /// The staff e-mail (audit).
    public string RequestedBy { get; private set; } = null!;

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string IdempotencyKey { get; private set; } = null!;

    public static Refund Request(Guid orderId, Guid paymentId, Money amount, string reason, string requestedBy,
        string idempotencyKey, DateTimeOffset now)
    {
        Guard.Against(!amount.IsPositive, "refund.amount_positive");
        var trimmed = Guard.NotEmpty(reason).Trim();
        return new Refund(Guid.CreateVersion7())
        {
            OrderId = Guard.NotEmpty(orderId),
            PaymentId = Guard.NotEmpty(paymentId),
            Amount = amount,
            Reason = trimmed.Length > 500 ? trimmed[..500] : trimmed,
            Status = RefundStatus.Requested,
            RequestedBy = Guard.NotEmpty(requestedBy),
            IdempotencyKey = Guard.NotEmpty(idempotencyKey),
            RequestedAt = now,
        };
    }

    public void Succeed(string? providerRefundId, DateTimeOffset now)
    {
        Guard.Against(Status != RefundStatus.Requested, "refund.not_requested");
        Status = RefundStatus.Succeeded;
        ProviderRefundId = providerRefundId;
        CompletedAt = now;
    }

    public void Fail(string? message, DateTimeOffset now)
    {
        Guard.Against(Status != RefundStatus.Requested, "refund.not_requested");
        Status = RefundStatus.Failed;
        FailureMessage = message is null ? null : message.Length > 200 ? message[..200] : message;
        CompletedAt = now;
    }
}
