namespace Ta3lom.Store.Domain.Entities.Documents;

/// Table `invoice` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class Invoice : AggregateRoot
{
    public Invoice(Guid orderId, InvoiceKind kind, string number, bool isTaxInvoice, Money netTotal, Money vatTotal, Money grossTotal, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        OrderId = Guard.NotEmpty(orderId);
        Kind = kind;
        Number = Guard.NotNull(number);
        IssuedAt = now;
        IsTaxInvoice = isTaxInvoice;
        NetTotal = netTotal;
        VatTotal = vatTotal;
        GrossTotal = grossTotal;
    }

    private Invoice()
    {
    }

    public Guid OrderId { get; private set; }

    public InvoiceKind Kind { get; private set; }

    public string Number { get; private set; } = null!;

    public DateTimeOffset IssuedAt { get; private set; }

    public bool IsTaxInvoice { get; private set; }

    public string? SellerVatNo { get; private set; }

    public Money NetTotal { get; private set; }

    public Money VatTotal { get; private set; }

    public Money GrossTotal { get; private set; }

    public Guid? ReferencesInvoiceId { get; private set; }

    public Guid? PdfAssetId { get; private set; }

    public Guid? ZatcaUuid { get; private set; }

    public long? ZatcaIcv { get; private set; }

    public string? ZatcaPih { get; private set; }

    public string? ZatcaHash { get; private set; }

    public string? ZatcaQr { get; private set; }

    public ZatcaStatus? ZatcaStatus { get; private set; }

    /// RUL-59: the invoice of the purchase lines (and shipping) of a paid order; a tax invoice only while VAT is enabled.
    public static Invoice ForOrder(Guid orderId, string number, bool vatEnabled, string? sellerVatNo, Money gross, Money vat, DateTimeOffset now) =>
        new(orderId, InvoiceKind.Invoice, number, vatEnabled, gross - vat, vat, gross, now)
        {
            SellerVatNo = vatEnabled ? sellerVatNo : null,
            ZatcaStatus = vatEnabled ? Enums.ZatcaStatus.Pending : Enums.ZatcaStatus.NotRequired,
        };

    public bool HasPdf => PdfAssetId is not null;

    /// 09 §9.1: documents are immutable; the rendered PDF is attached once (with the Phase-1 QR of a tax invoice).
    public void AttachPdf(Guid pdfAssetId, string? zatcaQr)
    {
        Guard.Against(PdfAssetId is not null, "document.already_rendered");
        PdfAssetId = pdfAssetId;
        ZatcaQr = zatcaQr;
    }
}
