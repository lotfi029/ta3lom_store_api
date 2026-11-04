namespace Ta3lom.Store.Domain.Entities.Payments;

/// Table `payment` (ADR-S05, F-D1): one attempt to pay an order on the gateway's hosted page. `Provider` records which
/// adapter took it, so the provider can change without a schema change. Card data never reaches us: only the provider's
/// ids, the amounts, the method and a failure code/message.
public sealed class Payment : AggregateRoot
{
    private Payment(Guid id) : base(id)
    {
    }

    private Payment()
    {
    }

    public Guid OrderId { get; private set; }

    /// 1 for the checkout's attempt, n+1 for each retry (`UNIQUE (order_id, attempt_no)`).
    public int AttemptNo { get; private set; }

    public string Provider { get; private set; } = null!;

    /// The gateway's payment id, known once the donor paid (or failed) on the hosted page.
    public string? ProviderPaymentId { get; private set; }

    /// The gateway's hosted-page id (Moyasar invoice id).
    public string? ProviderInvoiceId { get; private set; }

    public PaymentStatus Status { get; private set; }

    public Money Amount { get; private set; }

    public string Currency { get; private set; } = Money.Currency;

    public PaymentMethod Method { get; private set; }

    public string? RedirectUrl { get; private set; }

    public DateTimeOffset? RedirectExpiresAt { get; private set; }

    public string? FailureCode { get; private set; }

    /// The gateway's human message (e.g. "INSUFFICIENT FUNDS"); never a card number.
    public string? FailureMessage { get; private set; }

    public Money CapturedAmount { get; private set; } = Money.Zero;

    public Money RefundedAmount { get; private set; } = Money.Zero;

    public PaymentReviewReason? ReviewReason { get; private set; }

    public DateTimeOffset InitiatedAt { get; private set; }

    public DateTimeOffset? CapturedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public bool IsOpen => Status == PaymentStatus.Initiated;

    public bool IsCaptured => Status is PaymentStatus.Captured or PaymentStatus.Review or PaymentStatus.Refunded;

    public Money Refundable => IsCaptured ? CapturedAmount - RefundedAmount : Money.Zero;

    public static Payment Start(Guid orderId, int attemptNo, string provider, Money amount, DateTimeOffset now)
    {
        Guard.Against(attemptNo < 1, "payment.attempt_invalid");
        Guard.Against(!amount.IsPositive, "payment.amount_positive");
        return new Payment(Guid.CreateVersion7())
        {
            OrderId = Guard.NotEmpty(orderId),
            AttemptNo = attemptNo,
            Provider = Guard.NotEmpty(provider),
            Amount = amount,
            Status = PaymentStatus.Initiated,
            Method = PaymentMethod.Unknown,
            InitiatedAt = now,
            UpdatedAt = now,
        };
    }

    /// The gateway created the hosted page (outside the checkout transaction).
    public void AttachHostedPage(string providerInvoiceId, string redirectUrl, DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        Guard.Against(Status != PaymentStatus.Initiated, "payment.not_open");
        ProviderInvoiceId = Guard.NotEmpty(providerInvoiceId);
        RedirectUrl = Guard.NotEmpty(redirectUrl);
        RedirectExpiresAt = expiresAt;
        UpdatedAt = now;
    }

    /// The gateway confirmed a capture of `captured` (re-fetched, never from the webhook body alone). Idempotent.
    public void MarkCaptured(string providerPaymentId, Money captured, PaymentMethod method, DateTimeOffset now)
    {
        if (IsCaptured)
        {
            return;
        }

        Guard.Against(!captured.IsPositive, "payment.captured_positive");
        ProviderPaymentId = Guard.NotEmpty(providerPaymentId);
        CapturedAmount = captured;
        Method = method;
        Status = PaymentStatus.Captured;
        CapturedAt = now;
        FailureCode = null;
        FailureMessage = null;
        UpdatedAt = now;
    }

    /// Captured, but it could not settle the order (RUL-29/35).
    public void MarkForReview(PaymentReviewReason reason, DateTimeOffset now)
    {
        Guard.Against(!IsCaptured, "payment.not_captured");
        if (Status == PaymentStatus.Captured)
        {
            Status = PaymentStatus.Review;
        }

        ReviewReason = reason;
        UpdatedAt = now;
    }

    /// The gateway voided a payment we had taken as captured (review 04 A-1): nothing is left to refund; finance reviews.
    public void MarkVoidedAfterCapture(DateTimeOffset now)
    {
        Guard.Against(!IsCaptured, "payment.not_captured");
        RefundedAmount = CapturedAmount;
        Status = PaymentStatus.Review;
        ReviewReason = PaymentReviewReason.VoidedAfterCapture;
        UpdatedAt = now;
    }

    /// Finance accepted the reviewed payment.
    public void AcceptReview(DateTimeOffset now)
    {
        if (Status == PaymentStatus.Review)
        {
            Status = PaymentStatus.Captured;
            UpdatedAt = now;
        }
    }

    /// The donor's attempt failed on the hosted page. A failed event after a capture is ignored (out of order).
    public void MarkFailed(string? providerPaymentId, string? code, string? message, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Initiated)
        {
            return;
        }

        ProviderPaymentId ??= providerPaymentId;
        Status = PaymentStatus.Failed;
        FailureCode = Truncate(code, 64);
        FailureMessage = Truncate(message, 200);
        UpdatedAt = now;
    }

    /// A newer attempt replaced this one (best effort cancel at the gateway).
    public void Cancel(DateTimeOffset now)
    {
        if (Status == PaymentStatus.Initiated)
        {
            Status = PaymentStatus.Cancelled;
            UpdatedAt = now;
        }
    }

    public void ApplyRefund(Money amount, DateTimeOffset now)
    {
        Guard.Against(!IsCaptured, "payment.not_captured");
        Guard.Against(amount > Refundable || !amount.IsPositive, "payment.refund_exceeds");
        RefundedAmount += amount;
        if (RefundedAmount == CapturedAmount)
        {
            Status = PaymentStatus.Refunded;
        }

        UpdatedAt = now;
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
