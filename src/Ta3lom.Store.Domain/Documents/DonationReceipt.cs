using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Domain.Documents;

/// Table `donation_receipt` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class DonationReceipt : AggregateRoot
{
    public DonationReceipt(Guid orderId, string number, string donorName, Money totalAmount, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        OrderId = Guard.NotEmpty(orderId);
        Kind = ReceiptKind.Receipt;
        Number = Guard.NotNull(number);
        IssuedAt = now;
        DonorName = Guard.NotNull(donorName);
        TotalAmount = totalAmount;
        ZakatAmount = Money.Zero;
    }

    private DonationReceipt()
    {
    }

    public Guid OrderId { get; private set; }

    public ReceiptKind Kind { get; private set; }

    public string Number { get; private set; } = null!;

    public DateTimeOffset IssuedAt { get; private set; }

    public string DonorName { get; private set; } = null!;

    public Money TotalAmount { get; private set; }

    public Money ZakatAmount { get; private set; }

    public Guid? ReferencesReceiptId { get; private set; }

    public Guid? PdfAssetId { get; private set; }
}
