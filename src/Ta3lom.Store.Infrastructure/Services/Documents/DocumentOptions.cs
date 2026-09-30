using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Documents;

/// `Store:Documents` — the seller identity printed on invoices and receipts (09 §9.2). The VAT number is the
/// `seller_vat_no` setting; the licence number is the association's registration.
public sealed class DocumentOptions
{
    public const string Section = "Store:Documents";

    public string NameAr { get; set; } = "جمعية التعلم الإلكتروني";

    public string NameEn { get; set; } = "E-Learning Association";

    public string AddressAr { get; set; } = "الرياض، المملكة العربية السعودية";

    public string LicenceNo { get; set; } = "—";
}

internal sealed class SellerProfile(IOptions<DocumentOptions> options) : ISellerProfile
{
    public string NameAr => options.Value.NameAr;

    public string NameEn => options.Value.NameEn;

    public string AddressAr => options.Value.AddressAr;

    public string LicenceNo => options.Value.LicenceNo;
}

/// ZATCA Phase 2 is S2 (FR-S65): tax invoices stay `pending`.
internal sealed class NoopEInvoicingGateway : IEInvoicingGateway
{
    public Task<EInvoicingResult> ReportAsync(Guid invoiceId, CancellationToken ct = default) =>
        Task.FromResult(new EInvoicingResult(false, "pending", "Phase 2 reporting is not enabled (S2)."));
}
