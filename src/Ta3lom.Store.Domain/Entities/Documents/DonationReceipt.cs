namespace Ta3lom.Store.Domain.Entities.Documents;

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

    /// RUL-59, RUL-23: the donor's real name even when the gift is anonymous.
    public static DonationReceipt ForOrder(Guid orderId, string number, string donorName, Money total, Money zakat, DateTimeOffset now) =>
        new(orderId, number, donorName, total, now) { ZakatAmount = zakat };

    public bool HasPdf => PdfAssetId is not null;

    public void AttachPdf(Guid pdfAssetId)
    {
        Guard.Against(PdfAssetId is not null, "document.already_rendered");
        PdfAssetId = pdfAssetId;
    }
}
