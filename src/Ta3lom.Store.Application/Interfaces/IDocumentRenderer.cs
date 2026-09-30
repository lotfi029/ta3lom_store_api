namespace Ta3lom.Store.Application.Interfaces;

/// PDF documents (09 §9, RUL-59): QuestPDF in Infrastructure, IBM Plex Sans Arabic, RTL, Western digits.
public interface IDocumentRenderer
{
    byte[] RenderInvoice(InvoiceDocument document);

    byte[] RenderDonationReceipt(ReceiptDocument document);

    byte[] RenderPackingSlip(PackingSlipDocument document);
}

/// Seller identity printed on every document (`Store:Documents`).
public interface ISellerProfile
{
    string NameAr { get; }

    string NameEn { get; }

    string AddressAr { get; }

    string LicenceNo { get; }
}

/// A sales invoice or simplified tax invoice (09 §9.2). `QrBase64` only on a tax invoice.
public sealed record InvoiceDocument(string Number, DateTimeOffset IssuedAt, bool IsTaxInvoice, string? SellerVatNo, string OrderReference,
    string BuyerName, string BuyerMobile, string BuyerEmail, IReadOnlyList<InvoiceDocumentLine> Lines, string? ShippingGross, string? ShippingVat,
    string NetTotal, string VatTotal, string GrossTotal, decimal VatRate, string? QrBase64);

public sealed record InvoiceDocumentLine(string TitleAr, int Quantity, string UnitPrice, string VatAmount, string Gross);

/// A donation receipt (09 §9.2, RUL-20, RUL-23): the real donor name, zakat shown separately, gifts outside VAT.
public sealed record ReceiptDocument(string Number, DateTimeOffset IssuedAt, string OrderReference, string DonorName, bool IsAnonymous,
    IReadOnlyList<ReceiptDocumentLine> Lines, string Total, string ZakatAmount, IReadOnlyList<string> DedicatedTo);

/// `QuantityLabel`: "5 مقاعد" / "مبلغ"; `Intent`: صدقة | زكاة.
public sealed record ReceiptDocumentLine(string TitleAr, string QuantityLabel, string Intent, string Amount);

/// FR-S42: order reference, recipient, address snapshot and print lines — no prices; rendered on demand.
public sealed record PackingSlipDocument(string OrderReference, DateTimeOffset PrintedAt, string RecipientName, string RecipientMobile, string Address,
    IReadOnlyList<PackingSlipLine> Lines);

public sealed record PackingSlipLine(string Sku, string TitleAr, int Quantity);

/// ZATCA Phase 2 reporting (S2, FR-S65). `NoopEInvoicingGateway` until then: tax invoices stay `pending`.
public interface IEInvoicingGateway
{
    Task<EInvoicingResult> ReportAsync(Guid invoiceId, CancellationToken ct = default);
}

public sealed record EInvoicingResult(bool Reported, string Status, string? Detail);
