using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Domain.Entities.Ordering;

/// Table `orders` (RUL-24 … RUL-31). Created by checkout in `pending_payment`; settlement (S-16) moves it on.
public sealed class Order : AggregateRoot
{
    public const string ReferencePrefix = "T3-";

    private readonly List<OrderLine> _lines = [];

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

    /// Owned JSON snapshot (`orders.shipping_address` jsonb); required when a print line exists (RUL-26).
    public ShippingAddress? ShippingAddress { get; private set; }

    public Guid? SourceCartId { get; private set; }

    public DateTimeOffset TermsAcceptedAt { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    public DateTimeOffset PaymentDueAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? FulfilledAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    /// RUL-28: `T3-YY-NNNNNN` from the Riyadh year and the gap-tolerant `order_ref_seq`.
    public static string FormatReference(int year, long sequence) =>
        FormattableString.Invariant($"{ReferencePrefix}{year % 100:00}-{sequence:000000}");

    /// Checkout (database/01 §4.3): snapshots the repriced lines and dedications, totals per RUL-31, VAT per RUL-27.
    /// Returns the dedications to insert with the order (one per dedicated gift line, RUL-21/22).
    public static (Order Order, IReadOnlyList<Dedication> Dedications) Place(string reference, Guid customerId, OrderContact contact, string locale,
        bool isAnonymous, OrderShipping shipping, Guid sourceCartId, IReadOnlyList<OrderLineDraft> lines, DateTimeOffset now, DateTimeOffset paymentDueAt)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(shipping);
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new DomainException("An order needs at least one line.");
        }

        var purchases = lines.Where(l => l.Kind == LineKind.Buy).Select(l => l.Amount).Sum();
        var gifts = lines.Where(l => l.Kind != LineKind.Buy).Select(l => l.Amount).Sum();
        var grand = purchases + shipping.Fee + gifts;                                       // RUL-31
        if (!grand.IsPositive)
        {
            throw new DomainException("An order total must be positive.");
        }

        var order = new Order(reference, customerId, grand, contact.Name, contact.MobileE164, contact.Email, now, paymentDueAt, now)
        {
            PurchasesTotal = purchases,
            ShippingTotal = shipping.Fee,
            GiftsTotal = gifts,
            VatTotal = lines.Select(l => l.VatAmount).Sum() + shipping.Vat,              // informational, included in the prices
            IsAnonymous = isAnonymous && lines.Any(l => l.Kind != LineKind.Buy),            // RUL-23: only with gifts
            Locale = locale,
            ShippingAddress = shipping.Address,
            SourceCartId = sourceCartId,
        };

        List<Dedication> dedications = [];
        short lineNo = 0;
        foreach (var draft in lines)
        {
            var line = OrderLine.FromDraft(order.Id, ++lineNo, draft);
            order._lines.Add(line);
            if (draft.Kind != LineKind.Buy && draft.Dedication is { } d)
            {
                dedications.Add(Dedication.ForLine(line.Id, d));
            }
        }

        order.Raise(new OrderPlaced(order.Id, reference));
        return (order, dedications);
    }

    public static bool IsPaidState(OrderStatus s) =>
        s is OrderStatus.Paid or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded;

    /// RUL-33/35 (09 §5.3): called only from settlement (`IOrderSettlement`). A replay is `AlreadyPaid`; a capture on an
    /// expired/cancelled order or with another amount parks the order in `payment_review` with a `FinanceAlert`.
    public PaidOutcome MarkPaid(Money captured, DateTimeOffset now)
    {
        if (IsPaidState(Status))
        {
            return PaidOutcome.AlreadyPaid;
        }

        switch (Status)
        {
            case OrderStatus.Expired or OrderStatus.Cancelled:
                Status = OrderStatus.PaymentReview;
                Raise(new FinanceAlert(FinanceAlertKinds.LateCapture, Id, Reference, captured.ToWire()));
                return PaidOutcome.Review;
            case OrderStatus.PaymentReview:
                return PaidOutcome.Review;
        }

        if (captured != GrandTotal)
        {
            Status = OrderStatus.PaymentReview;
            Raise(new FinanceAlert(FinanceAlertKinds.AmountMismatch, Id, Reference, $"captured {captured.ToWire()}, expected {GrandTotal.ToWire()}"));
            return PaidOutcome.Review;
        }

        Status = OrderStatus.Paid;
        PaidAt = now;
        return PaidOutcome.Paid;
    }

    /// After every effect ran: the order-level outbox messages (RUL-39, RUL-59, RUL-63) and the fulfilment roll-up.
    public void CompleteSettlement(DateTimeOffset now)
    {
        RecomputeFulfilment(now);
        Raise(new OrderPaid(Id, Reference));
        Raise(new RenderDocuments(Id));
    }

    /// RUL-40: `fulfilled` once every line is in its terminal state.
    public void RecomputeFulfilment(DateTimeOffset now)
    {
        if (Status == OrderStatus.Paid && _lines.Count > 0 && _lines.All(l => l.IsTerminal))
        {
            Status = OrderStatus.Fulfilled;
            FulfilledAt = now;
        }
    }

    /// Contact text left on the orders of a deleted account (database/03 §7).
    public const string DeletedContact = "محذوف / deleted";

    /// PDPL erasure (database/03 §7): the financial record stays, the person does not — contact and recipient replaced;
    /// the city is kept for the shipping record.
    public void AnonymiseContact()
    {
        ContactName = DeletedContact;
        ContactMobile = DeletedContact;
        ContactEmail = DeletedContact;
        if (ShippingAddress is { } a)
        {
            ShippingAddress = new ShippingAddress(DeletedContact, DeletedContact, a.City, null, null, null, null, null);
        }
    }

    public bool CanBeCancelled => Status is OrderStatus.PendingPayment or OrderStatus.PaymentFailed;

    /// RUL-29: staff cancel, only from `pending_payment` / `payment_failed`. The caller releases the stock (RUL-30).
    public bool Cancel(string reason, DateTimeOffset now)
    {
        if (!CanBeCancelled)
        {
            return false;
        }

        Status = OrderStatus.Cancelled;
        CancelledAt = now;
        Raise(new OrderCancelled(Id, Reference, reason));
        return true;
    }

    /// RUL-29: no success within the payment window. The caller releases stock (RUL-30) and restores the cart.
    public bool Expire(DateTimeOffset now, bool cartRestored)
    {
        if (!CanBeCancelled || now < PaymentDueAt)
        {
            return false;
        }

        Status = OrderStatus.Expired;
        Raise(new OrderExpired(Id, Reference, cartRestored));
        return true;
    }

    /// Print quantities still reserved for this order (RUL-30).
    public IReadOnlyList<(Guid BookFormatId, int Quantity)> PrintReservations(IReadOnlySet<Guid> printFormatIds) =>
        [.. _lines.Where(l => l.Kind == LineKind.Buy && l.BookFormatId is { } f && printFormatIds.Contains(f)).Select(l => (l.BookFormatId!.Value, l.Quantity))];

    /// Raised on behalf of a settlement effect so it lands in the same transaction.
    public void RaiseEvent(IDomainEvent domainEvent) => Raise(domainEvent);
}
