using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Documents;

/// RUL-41, P0-4: the stamp text is rendered by QuestPDF (shaped Arabic, RTL) once per page size and drawn on every page by
/// PdfSharp as a form XObject; PdfSharp's own text cannot shape Arabic. ~0.2 s for 300 pages.
internal sealed class PdfSharpWatermarker : IPdfWatermarker
{
    public async Task<byte[]> WatermarkAsync(Stream source, PdfWatermark watermark, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(watermark);
        QuestPdfDocumentRenderer.EnsureFonts();
        using var input = new MemoryStream();
        await source.CopyToAsync(input, ct);
        input.Position = 0;

        using var book = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var text = $"نسخة خاصة بـ {watermark.BuyerName} — {QuestPdfDocumentRenderer.Ltr(watermark.MaskedMobile)} — {watermark.OrderReference} — لا يجوز نشرها";
        var forms = new Dictionary<(int, int), XPdfForm>();
        try
        {
            foreach (var page in book.Pages)
            {
                var size = ((int)Math.Round(page.Width.Point), (int)Math.Round(page.Height.Point));
                if (!forms.TryGetValue(size, out var form))
                {
                    form = XPdfForm.FromStream(new MemoryStream(Stamp(text, size.Item1, size.Item2)));
                    forms[size] = form;
                }

                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                gfx.DrawImage(form, 0, 0, page.Width.Point, page.Height.Point);
            }

            book.Info.Subject = "Licensed copy " + watermark.OrderReference;
            using var output = new MemoryStream();
            book.Save(output);
            return output.ToArray();
        }
        finally
        {
            foreach (var form in forms.Values)
            {
                form.Dispose();
            }
        }
    }

    private static byte[] Stamp(string text, int widthPt, int heightPt) => Document.Create(doc => doc.Page(page =>
    {
        page.Size(widthPt, heightPt, Unit.Point);
        page.Margin(0);
        page.ContentFromRightToLeft();
        page.DefaultTextStyle(t => t.FontFamily("IBM Plex Sans Arabic", "Noto Naskh Arabic").FontSize(8).FontColor(Colors.Grey.Medium));
        page.Content().AlignBottom().PaddingBottom(10).PaddingHorizontal(12).AlignCenter().Text(text);
    })).GeneratePdf();
}
