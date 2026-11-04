using System.Reflection;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Receipts;

/// DR / DRC receipt PDF (F-D4) with QuestPDF (Community licence — a non-profit, DECISIONS F3a). Right-to-left, Noto
/// Naskh Arabic embedded (SIL OFL, Resources/Fonts/OFL.txt) so Arabic is shaped and joined; English lines under the
/// Arabic ones. No donor mobile or e-mail on the document.
internal sealed class QuestPdfReceiptRenderer : IReceiptRenderer
{
    public const string FontFamily = "Noto Naskh Arabic";
    private static readonly Lock InitLock = new();
    private static bool _initialised;
    private static readonly string[] HeaderLabels = ["البيان", "العدد", "المبلغ للوحدة", "المبلغ"];

    public QuestPdfReceiptRenderer() => EnsureInitialised();

    public byte[] Render(ReceiptDocument d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.ContentFromRightToLeft();
            page.DefaultTextStyle(t => t.FontFamily(FontFamily).FontSize(11).FontColor(Colors.Grey.Darken4));

            page.Header().Column(col =>
            {
                col.Item().Text(d.OrgNameAr).Bold().FontSize(16);
                if (d.OrgNameEn is { Length: > 0 } en)
                {
                    col.Item().Text(en).FontSize(10).FontColor(Colors.Grey.Darken1);
                }

                if (d.LicenceNo is { Length: > 0 } licence)
                {
                    col.Item().Text($"ترخيص رقم {licence}").FontSize(10);
                }

                col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            });

            page.Content().PaddingVertical(12).Column(col =>
            {
                col.Spacing(6);
                if (d.VoidedByNumber is { } voidedBy)
                {
                    // Review 04 R-1: a refunded donation's DR is void; it names the cancellation receipt.
                    col.Item().Border(1.5f).BorderColor(Colors.Red.Darken2).Background(Colors.Red.Lighten5).Padding(8).Column(banner =>
                    {
                        banner.Item().Text(t =>
                        {
                            t.Span("ملغى — ").Bold().FontSize(16).FontColor(Colors.Red.Darken3);
                            t.Span(voidedBy).Bold().FontSize(16).FontColor(Colors.Red.Darken3);
                        });
                        banner.Item().Text($"Void — cancelled by {voidedBy}").FontSize(9).FontColor(Colors.Red.Darken3);
                    });
                }

                col.Item().Text(d.IsCancellation ? "إيصال إلغاء تبرع" : "إيصال تبرع").Bold().FontSize(18);
                col.Item().Text(d.IsCancellation ? "Donation cancellation receipt" : "Donation receipt").FontSize(10).FontColor(Colors.Grey.Darken1);
                col.Item().Text(t =>
                {
                    t.Span("رقم الإيصال: ").SemiBold();
                    t.Span(d.Number);
                });
                if (d.VoidsNumber is { } voids)
                {
                    col.Item().Text(t =>
                    {
                        t.Span("يلغي الإيصال رقم: ").SemiBold();
                        t.Span(voids);
                    });
                }

                col.Item().Text(t =>
                {
                    t.Span("رقم الطلب: ").SemiBold();
                    t.Span(d.OrderReference);
                });
                col.Item().Text(t =>
                {
                    t.Span("التاريخ: ").SemiBold();
                    t.Span($"{d.IssuedGregorian} م — {d.IssuedHijri} هـ");
                });
                col.Item().Text(t =>
                {
                    t.Span("اسم المتبرع: ").SemiBold();
                    t.Span(d.DonorName);
                });

                col.Item().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(6);
                        c.RelativeColumn(1);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                    });
                    table.Header(h =>
                    {
                        foreach (var label in HeaderLabels)
                        {
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(label).SemiBold();
                        }
                    });
                    foreach (var line in d.Lines)
                    {
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(line.Description);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(line.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(line.UnitAmount);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(line.Amount);
                    }
                });

                col.Item().PaddingTop(8).Text(t =>
                {
                    t.Span("الإجمالي: ").Bold();
                    t.Span($"{d.Total} ريال سعودي").Bold();
                });
                col.Item().Text(d.TotalInWords);
                if (d.VatNoteAr is { Length: > 0 } vat)
                {
                    col.Item().PaddingTop(6).Text(vat).FontSize(10);
                }

                if (d.VatNoteEn is { Length: > 0 } vatEn)
                {
                    col.Item().Text(vatEn).FontSize(9).FontColor(Colors.Grey.Darken1);
                }
            });

            page.Footer().Column(col =>
            {
                col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                col.Item().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text("التوقيع والختم").FontSize(10);
                    if (d.SignatureImage is { Length: > 0 } image)
                    {
                        row.ConstantItem(120).Height(60).Image(image).FitArea();
                    }
                });
                col.Item().AlignCenter().Text("هذا الإيصال صادر إلكترونيًا").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        })).GeneratePdf();
    }

    private static void EnsureInitialised()
    {
        lock (InitLock)
        {
            if (_initialised)
            {
                return;
            }

            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.UseSystemFonts = false;
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var name in new[] { "Fonts.NotoNaskhArabic-Regular.ttf", "Fonts.NotoNaskhArabic-Bold.ttf" })
            {
                using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Embedded font {name} is missing.");
                FontManager.RegisterFontFromStream(stream);   // family name from the file: "Noto Naskh Arabic"
            }

            _initialised = true;
        }
    }
}
