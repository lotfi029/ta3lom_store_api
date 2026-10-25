using System.Globalization;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Domain.Entities.Donations;

/// Table `orders` (BR-A08, D-09): a donation order with re-priced line snapshots. It starts in `pending_payment`; staff may
/// cancel it while it is pending. The payment states arrive with R5-Payments.
public sealed class Order : AggregateRoot
{
    private readonly List<OrderLine> _lines = [];

    private Order(Guid id, DateTimeOffset now) : base(id)
    {
        Status = OrderStatus.PendingPayment;
        CreatedAt = now;
        UpdatedAt = now;
    }

    private Order()
    {
    }

    public string Reference { get; private set; } = null!;

    public Guid CustomerId { get; private set; }

    public OrderStatus Status { get; private set; }

    public string DonorName { get; private set; } = null!;

    public string DonorEmail { get; private set; } = null!;

    /// Snapshot of the donor's mobile at checkout.
    public string DonorMobile { get; private set; } = null!;

    public string Locale { get; private set; } = null!;

    public Money TotalAmount { get; private set; }

    public int LineCount { get; private set; }

    /// SHA-256 of the status token handed to the browser (the guest status page).
    public string StatusTokenHash { get; private set; } = null!;

    /// The `Idempotency-Key` of the checkout that placed it (unique per donor): a retry after a crash finds this order (A-5).
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset PaymentDueAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancelledBy { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    /// D-09: `T3-YY-NNNNNN` — the Riyadh year and the next value of `order_ref_seq`.
    public static string FormatReference(int riyadhYear, long sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"T3-{riyadhYear % 100:D2}-{sequence:D6}");

    public static Order Place(string reference, Guid customerId, DonorContact donor, IReadOnlyList<OrderLineDraft> lines,
        string statusTokenHash, DateTimeOffset paymentDueAt, DateTimeOffset now, string? idempotencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(donor);
        ArgumentNullException.ThrowIfNull(lines);
        Guard.Against(lines.Count == 0, "order.lines_required");
        Guard.Against(paymentDueAt <= now, "order.payment_due_invalid");
        var order = new Order(Guid.CreateVersion7(), now)
        {
            Reference = Guard.NotEmpty(reference),
            CustomerId = Guard.NotEmpty(customerId),
            DonorName = Guard.NotEmpty(donor.Name).Trim(),
            DonorEmail = Guard.NotEmpty(donor.Email).Trim(),
            DonorMobile = Guard.NotEmpty(donor.Mobile),
            Locale = donor.Locale is "en" ? "en" : "ar",
            StatusTokenHash = Guard.NotEmpty(statusTokenHash),
            PaymentDueAt = paymentDueAt,
            IdempotencyKey = idempotencyKey,
        };
        short lineNo = 1;
        foreach (var draft in lines)
        {
            order._lines.Add(new OrderLine(order.Id, lineNo++, draft));
        }

        order.TotalAmount = order._lines.Select(l => l.Amount).Sum();
        order.LineCount = order._lines.Count;
        order.Raise(new OrderPlaced(order.Id, order.Reference, order.TotalAmount.ToWire(), order.LineCount));
        return order;
    }

    /// D-09: staff cancel a pending order.
    public Result Cancel(string cancelledBy, DateTimeOffset now)
    {
        if (Status != OrderStatus.PendingPayment)
        {
            return OrderErrors.CancelNotAllowed(Status);
        }

        Status = OrderStatus.Cancelled;
        CancelledAt = now;
        CancelledBy = Guard.NotEmpty(cancelledBy);
        UpdatedAt = now;
        Raise(new OrderCancelled(Id, Reference, cancelledBy));
        return Result.Success();
    }

    /// D-14: deleting a donor anonymises the contact snapshot; the order and its amounts stay.
    public void AnonymiseDonor(DateTimeOffset now)
    {
        DonorName = AnonymisedName;
        DonorEmail = AnonymisedEmail;
        DonorMobile = AnonymisedMobile;
        UpdatedAt = now;
    }

    public const string AnonymisedName = "—";
    public const string AnonymisedEmail = "deleted@invalid";
    public const string AnonymisedMobile = "deleted";
}

/// The donor's contact confirmed at checkout (BR-A08).
public sealed record DonorContact(string Name, string Email, string Mobile, string Locale);

/// A re-priced line, ready to be snapshotted on the order.
public sealed record OrderLineDraft(TargetKind TargetKind, Guid? ProjectId, Guid? CategoryId, Guid? OptionId,
    string TitleAr, string? TitleEn, string? OptionLabelAr, string? OptionLabelEn, int Quantity, Money UnitAmount);
