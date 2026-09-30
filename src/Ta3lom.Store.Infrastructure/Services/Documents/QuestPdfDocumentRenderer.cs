using System.Globalization;
using QRCoder;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Documents;

/// 09 §9, P0-3: QuestPDF (Community licence), IBM Plex Sans Arabic with Noto Naskh Arabic fallback, RTL pages, Western
/// digits; the title is bilingual (English stacked under Arabic), inline labels are Arabic only because mixed labels reorder
/// under the bidi algorithm (P0-3). Mixed-direction values (phones, e-mails, English) are wrapped in LRI/PDI.
internal sealed class QuestPdfDocumentRenderer(ISellerProfile seller, IClock clock) : IDocumentRenderer
{
    private const string Primary = "IBM Plex Sans Arabic";
    private const string Fallback = "Noto Naskh Arabic";
    private static readonly Lock FontLock = new();
    private static bool _fontsRegistered;

    public byte[] RenderInvoice(InvoiceDocument d)
    {
        ArgumentNullException.ThrowIfNull(d);
        EnsureFonts();
        return Document.Create(doc => doc.Page(page =>
        {
            Page(page);
            page.Header().Element(h => Header(h, d.IsTaxInvoice ? "فاتورة ضريبية مبسطة" : "فاتورة",
                d.IsTaxInvoice ? "Simplified tax invoice" : "Invoice", d.Number));
            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(8);
                col.Item().Text(t =>
                {
                    t.Span("رقم الطلب: ").SemiBold();
                    t.Span(d.OrderReference);
                    t.Span("  •  التاريخ: ").SemiBold();
                    t.Span(Date(d.IssuedAt));
                });
                col.Item().Text($"العميل: {d.BuyerName} — {Ltr(d.BuyerMobile)} — {Ltr(d.BuyerEmail)}");
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(5);
                        c.RelativeColumn(1);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                    });
                    table.Header(h =>
                    {
                        HeaderCell(h, "البند");
                        HeaderCell(h, "الكمية");
                        HeaderCell(h, "سعر الوحدة");
                        HeaderCell(h, d.IsTaxInvoice ? $"الضريبة {Rate(d.VatRate)}٪" : "الضريبة");
                        HeaderCell(h, "المبلغ");
                    });
                    foreach (var l in d.Lines)
                    {
                        Row(table, l.TitleAr, l.Quantity.ToString(CultureInfo.InvariantCulture), l.UnitPrice, l.VatAmount, l.Gross);
                    }

                    if (d.ShippingGross is { } shipping)
                    {
                        Row(table, "الشحن داخل المملكة", "1", shipping, d.ShippingVat ?? "0.00", shipping);
                    }
                });
                col.Item().AlignLeft().Width(240).Column(t =>
                {
                    Total(t, "الإجمالي قبل الضريبة", d.NetTotal);
                    Total(t, "ضريبة القيمة المضافة", d.VatTotal);
                    Total(t, "الإجمالي", d.GrossTotal, bold: true);
                });
                col.Item().Row(r =>
                {
                    r.RelativeItem().Column(c =>
                    {
                        if (d.IsTaxInvoice && d.SellerVatNo is { } vatNo)
                        {
                            c.Item().Text($"الرقم الضريبي للبائع: {vatNo}");
                        }
                        else
                        {
                            c.Item().Text("هذه ليست فاتورة ضريبية.").FontSize(9);
                        }

                        c.Item().Text("الأسعار بالريال السعودي وتشمل ضريبة القيمة المضافة إن وجدت.").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                    if (d.QrBase64 is { } qr)
                    {
                        r.ConstantItem(96).Image(QrPng(qr));
                    }
                });
            });
            Footer(page);
        })).GeneratePdf();
    }

    public byte[] RenderDonationReceipt(ReceiptDocument d)
    {
        ArgumentNullException.ThrowIfNull(d);
        EnsureFonts();
        return Document.Create(doc => doc.Page(page =>
        {
            Page(page);
            page.Header().Element(h => Header(h, "إيصال تبرع", "Donation receipt", d.Number));
            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(8);
                col.Item().Text($"تشكر {seller.NameAr} المتبرع الكريم على عطائه.").FontSize(12);
                col.Item().Text(t =>
                {
                    t.Span("المتبرع: ").SemiBold();
                    t.Span(d.DonorName);                                                    // RUL-23: the real name, even if anonymous
                });
                col.Item().Text(t =>
                {
                    t.Span("رقم الطلب: ").SemiBold();
                    t.Span(d.OrderReference);
                    t.Span("  •  التاريخ: ").SemiBold();
                    t.Span(Date(d.IssuedAt));
                });
                if (d.IsAnonymous)
                {
                    col.Item().Text("أُهدي باسم «فاعل خير»؛ يظهر الاسم في هذا الإيصال فقط.").FontSize(9);
                }

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(5);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                        c.RelativeColumn(2);
                    });
                    table.Header(h =>
                    {
                        HeaderCell(h, "المشروع");
                        HeaderCell(h, "الوحدات");
                        HeaderCell(h, "النية");
                        HeaderCell(h, "المبلغ");
                    });
                    foreach (var l in d.Lines)
                    {
                        Row(table, l.TitleAr, l.QuantityLabel, l.Intent, l.Amount);
                    }
                });
                col.Item().Text(t =>
                {
                    t.Span("الإجمالي: ").SemiBold();
                    t.Span(d.Total).SemiBold();
                });
                if (d.ZakatAmount != "0.00")
                {
                    col.Item().Text($"منها زكاة: {d.ZakatAmount} ر.س").SemiBold();
                }

                if (d.DedicatedTo.Count > 0)
                {
                    col.Item().Text("إهداء إلى: " + string.Join("، ", d.DedicatedTo));
                }

                col.Item().Text("هذا الإيصال ليس فاتورة ضريبية؛ التبرعات خارج نطاق ضريبة القيمة المضافة.").FontSize(9).FontColor(Colors.Grey.Darken1);
            });
            Footer(page);
        })).GeneratePdf();
    }

    /// U+2066 … U+2069 (P0-3): keeps `+966 …`, e-mails and English runs intact inside Arabic lines.
    internal static string Ltr(string value) => "⁦" + value + "⁩";

    private static string Rate(decimal rate) => rate.ToString("0.##", CultureInfo.InvariantCulture);

    private string Date(DateTimeOffset at) => clock.ToRiyadh(at).ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);

    private static void Page(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.Margin(36);
        page.ContentFromRightToLeft();
        page.DefaultTextStyle(s => s.FontFamily(Primary, Fallback).FontSize(10.5f));
    }

    private void Header(IContainer h, string titleAr, string titleEn, string number) => h.BorderBottom(1).PaddingBottom(6).Row(r =>
    {
        r.RelativeItem().Column(c =>
        {
            c.Item().Text($"{seller.NameAr} — متجر تعلّم").Bold().FontSize(14);
            c.Item().Text(Ltr(seller.NameEn)).FontSize(9);
            c.Item().Text(seller.AddressAr).FontSize(9);
        });
        r.RelativeItem().AlignLeft().Column(c =>
        {
            c.Item().AlignLeft().Text(titleAr).Bold().FontSize(16);
            c.Item().AlignLeft().Text(titleEn).FontSize(9);
            c.Item().AlignLeft().Text(number).FontSize(11);
        });
    });

    private void Footer(PageDescriptor page) => page.Footer().Row(r =>
    {
        r.RelativeItem().Text($"{seller.NameAr} — ترخيص رقم {seller.LicenceNo}").FontSize(8);
        r.AutoItem().Text(t =>
        {
            t.Span("صفحة ").FontSize(8);
            t.CurrentPageNumber().FontSize(8);
            t.Span(" من ").FontSize(8);
            t.TotalPages().FontSize(8);
        });
    });

    private static void HeaderCell(TableCellDescriptor h, string title) => h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(title).SemiBold();

    private static void Row(TableDescriptor t, params string[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = t.Cell().BorderBottom(0.5f).Padding(4);
            (i == 0 ? cell : cell.AlignLeft()).Text(cells[i]);
        }
    }

    private static void Total(ColumnDescriptor c, string label, string value, bool bold = false) => c.Item().Row(r =>
    {
        var l = r.RelativeItem().Text(label);
        var v = r.AutoItem().Text(value);
        if (bold)
        {
            l.Bold();
            v.Bold();
        }
    });

    private static byte[] QrPng(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(8);
    }

    /// Community licence (P0-3, REVIEW) and the embedded fonts, once per process.
    private static void EnsureFonts()
    {
        lock (FontLock)
        {
            if (_fontsRegistered)
            {
                return;
            }

            QuestPDF.Settings.License = LicenseType.Community;
            var assembly = typeof(QuestPdfDocumentRenderer).Assembly;
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Fonts.", StringComparison.Ordinal)))
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                FontManager.RegisterFontFromStream(stream);
            }

            _fontsRegistered = true;
        }
    }
}
