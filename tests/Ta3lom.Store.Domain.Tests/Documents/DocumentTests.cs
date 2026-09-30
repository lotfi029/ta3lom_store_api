using Ta3lom.Store.Domain.Entities.Documents;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Documents;

/// Sales invoices, donation receipts and their counters (RUL-23, RUL-27, RUL-59).
public sealed class DocumentTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrderId = Guid.CreateVersion7();

    [Fact]
    [Rule("RUL-59")]
    public void A_document_counter_starts_at_zero_per_series_and_year()
    {
        var c = new DocumentCounter(DocumentSeries.Inv, 2026);

        c.Series.ShouldBe(DocumentSeries.Inv);
        c.Year.ShouldBe(2026);
        c.LastValue.ShouldBe(0);
    }

    [Fact]
    [Rule("RUL-27")]
    public void With_vat_enabled_the_invoice_is_a_tax_invoice_awaiting_zatca()
    {
        var i = Invoice.ForOrder(OrderId, "INV-2026-000001", true, "300000000000003", new Money(350m), new Money(45.65m), T0);

        i.OrderId.ShouldBe(OrderId);
        i.Kind.ShouldBe(InvoiceKind.Invoice);
        i.Number.ShouldBe("INV-2026-000001");
        i.IssuedAt.ShouldBe(T0);
        i.IsTaxInvoice.ShouldBeTrue();
        i.SellerVatNo.ShouldBe("300000000000003");
        i.GrossTotal.ShouldBe(new Money(350m));
        i.VatTotal.ShouldBe(new Money(45.65m));
        i.NetTotal.ShouldBe(new Money(304.35m));
        i.ZatcaStatus.ShouldBe(ZatcaStatus.Pending);
        i.ReferencesInvoiceId.ShouldBeNull();
        i.HasPdf.ShouldBeFalse();
    }

    [Fact]
    [Rule("RUL-27")]
    public void With_vat_disabled_the_invoice_is_not_a_tax_invoice()
    {
        var i = Invoice.ForOrder(OrderId, "INV-2026-000002", false, "300000000000003", new Money(350m), Money.Zero, T0);

        i.IsTaxInvoice.ShouldBeFalse();
        i.SellerVatNo.ShouldBeNull();
        i.ZatcaStatus.ShouldBe(ZatcaStatus.NotRequired);
        i.NetTotal.ShouldBe(new Money(350m));
    }

    [Fact]
    [Rule("RUL-59")]
    public void A_document_pdf_is_attached_once()
    {
        var i = Invoice.ForOrder(OrderId, "INV-2026-000003", true, "3", new Money(10m), new Money(1.30m), T0);
        var pdf = Guid.CreateVersion7();

        i.AttachPdf(pdf, "qr-base64");

        i.HasPdf.ShouldBeTrue();
        i.PdfAssetId.ShouldBe(pdf);
        i.ZatcaQr.ShouldBe("qr-base64");
        Should.Throw<DomainException>(() => i.AttachPdf(Guid.CreateVersion7(), null)).Code.ShouldBe("document.already_rendered");
        i.PdfAssetId.ShouldBe(pdf);
    }

    [Fact]
    public void An_invoice_needs_its_order_and_number()
    {
        Should.Throw<DomainException>(() => new Invoice(Guid.Empty, InvoiceKind.CreditNote, "CN-1", false, Money.Zero, Money.Zero, Money.Zero, T0))
            .Code.ShouldBe("guard.empty");
        new Invoice(OrderId, InvoiceKind.CreditNote, "CN-2026-000001", false, Money.Zero, Money.Zero, Money.Zero, T0).Kind.ShouldBe(InvoiceKind.CreditNote);
    }

    [Fact]
    [Rule("RUL-23")]
    public void A_donation_receipt_carries_the_donor_real_name_and_the_zakat_share()
    {
        var r = DonationReceipt.ForOrder(OrderId, "DR-2026-000001", "نورة العتيبي", new Money(800m), new Money(350m), T0);

        r.OrderId.ShouldBe(OrderId);
        r.Kind.ShouldBe(ReceiptKind.Receipt);
        r.Number.ShouldBe("DR-2026-000001");
        r.IssuedAt.ShouldBe(T0);
        r.DonorName.ShouldBe("نورة العتيبي");
        r.TotalAmount.ShouldBe(new Money(800m));
        r.ZakatAmount.ShouldBe(new Money(350m));
        r.ReferencesReceiptId.ShouldBeNull();
        r.HasPdf.ShouldBeFalse();
        new DonationReceipt(OrderId, "DR-2", "n", new Money(1m), T0).ZakatAmount.ShouldBe(Money.Zero);
    }

    [Fact]
    [Rule("RUL-59")]
    public void A_receipt_pdf_is_attached_once()
    {
        var r = DonationReceipt.ForOrder(OrderId, "DR-2026-000002", "n", new Money(1m), Money.Zero, T0);
        var pdf = Guid.CreateVersion7();

        r.AttachPdf(pdf);

        r.HasPdf.ShouldBeTrue();
        r.PdfAssetId.ShouldBe(pdf);
        Should.Throw<DomainException>(() => r.AttachPdf(Guid.CreateVersion7())).Code.ShouldBe("document.already_rendered");
        Should.Throw<DomainException>(() => new DonationReceipt(OrderId, "DR", null!, Money.Zero, T0)).Code.ShouldBe("guard.null");
    }
}
