namespace Ta3lom.Store.Domain.Entities.Ordering;

/// Table `orders` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class Order : AggregateRoot
{
    public Order(string reference, Guid customerId, Money grandTotal, string contactName, string contactMobile, string contactEmail, DateTimeOffset termsAcceptedAt, DateTimeOffset paymentDueAt, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        Reference = Guard.NotNull(reference);
        CustomerId = Guard.NotEmpty(customerId);
        Status = OrderStatus.PendingPayment;
        Currency = "SAR";
        PurchasesTotal = Money.Zero;
        ShippingTotal = Money.Zero;
        GiftsTotal = Money.Zero;
        VatTotal = Money.Zero;
        GrandTotal = grandTotal;
        RefundedTotal = Money.Zero;
        IsAnonymous = false;
        Locale = "ar";
        ContactName = Guard.NotNull(contactName);
        ContactMobile = Guard.NotNull(contactMobile);
        ContactEmail = Guard.NotNull(contactEmail);
        TermsAcceptedAt = termsAcceptedAt;
        PlacedAt = now;
        PaymentDueAt = paymentDueAt;
    }

    private Order()
    {
    }

    public string Reference { get; private set; } = null!;

    public Guid CustomerId { get; private set; }

    public OrderStatus Status { get; private set; }

    public string Currency { get; private set; } = null!;

    public Money PurchasesTotal { get; private set; }

    public Money ShippingTotal { get; private set; }

    public Money GiftsTotal { get; private set; }

    public Money VatTotal { get; private set; }

    public Money GrandTotal { get; private set; }

    public Money RefundedTotal { get; private set; }

    public bool IsAnonymous { get; private set; }

    public string Locale { get; private set; } = null!;

    public string ContactName { get; private set; } = null!;

    public string ContactMobile { get; private set; } = null!;

    public string ContactEmail { get; private set; } = null!;

    public string? ShippingAddress { get; private set; }

    public Guid? SourceCartId { get; private set; }

    public DateTimeOffset TermsAcceptedAt { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    public DateTimeOffset PaymentDueAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? FulfilledAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }
}
