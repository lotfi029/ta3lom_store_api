// P0-4 — can PdfSharp stamp an Arabic buyer name + masked mobile + order reference on every page of a 300-page book fast
// enough, and how big does the file get? Compares PdfSharp text (no Arabic shaping) with a QuestPDF-rendered stamp
// imported once as a form XObject and drawn on every page.
using System.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;
var fonts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Ta3lom.Store.Infrastructure/Resources/Fonts"));
foreach (var f in Directory.GetFiles(fonts, "*.ttf"))
{
    using var s = File.OpenRead(f);
    FontManager.RegisterFontFromStream(s);
}

var outDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/spikes/P0-4"));
Directory.CreateDirectory(outDir);
const string Name = "عبدالرحمن بن سعيد آل مُحمّد";
const string Mobile = "05•••••567";
const string Reference = "T3-26-004187";

// 1. A 300-page "book" (text + a drawn figure per page), like a typical digital title.
var sw = Stopwatch.StartNew();
var bookPath = Path.Combine(outDir, "book-300.pdf");
Document.Create(doc => doc.Page(page =>
{
    page.Size(PageSizes.A5);
    page.Margin(30);
    page.ContentFromRightToLeft();
    page.DefaultTextStyle(t => t.FontFamily("IBM Plex Sans Arabic").FontSize(11));
    page.Content().Column(col =>
    {
        for (var i = 0; i < 300; i++)
        {
            col.Item().PageBreak();
            col.Item().Text($"الفصل {i + 1}").Bold().FontSize(16);
            col.Item().Text(string.Concat(Enumerable.Repeat("التعلم الإلكتروني يفتح أبواب المعرفة لكل طالب في كل مكان. ", 18)));
            col.Item().Height(120).Background(Colors.Grey.Lighten3).AlignCenter().AlignMiddle().Text($"شكل {i + 1}");
        }
    });
})).GeneratePdf(bookPath);
Console.WriteLine($"book: {new FileInfo(bookPath).Length / 1024} KB, generated in {sw.ElapsedMilliseconds} ms");

// 2. The stamp, rendered once by QuestPDF (HarfBuzz shaping, RTL) on a page the size of the book page.
var stamp = Document.Create(doc => doc.Page(page =>
{
    page.Size(PageSizes.A5);
    page.Margin(0);
    page.ContentFromRightToLeft();
    page.DefaultTextStyle(t => t.FontFamily("IBM Plex Sans Arabic").FontSize(8).FontColor(Colors.Grey.Medium));
    page.Content().AlignBottom().PaddingBottom(10).AlignCenter()
        .Text($"نسخة خاصة بـ {Name} — ⁦{Mobile}⁩ — {Reference} — لا يجوز نشرها");
})).GeneratePdf();

// 3a. QuestPDF stamp as a form XObject: import once, draw on every page (the XObject is stored once in the file).
foreach (var (label, run) in new (string, Func<string, string>)[] { ("form", StampWithForm), ("pdfsharp-text", StampWithText) })
{
    var times = new List<long>();
    string output = "";
    for (var i = 0; i < 3; i++)
    {
        sw.Restart();
        output = run(Path.Combine(outDir, $"book-300-{label}.pdf"));
        times.Add(sw.ElapsedMilliseconds);
    }

    Console.WriteLine($"{label}: {string.Join(" / ", times)} ms (cold / warm / warm), {new FileInfo(output).Length / 1024} KB");
}

string StampWithForm(string path)
{
    using var book = PdfReader.Open(bookPath, PdfDocumentOpenMode.Modify);
    using var stampStream = new MemoryStream(stamp);
    var form = XPdfForm.FromStream(stampStream);
    foreach (var page in book.Pages)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        gfx.DrawImage(form, 0, 0, page.Width.Point, page.Height.Point);
    }

    book.Info.Subject = $"Licensed to {Reference}";
    book.Save(path);
    return path;
}

// 3b. PdfSharp text for comparison (needs a font resolver; Arabic is NOT shaped: letters come out isolated and LTR).
string StampWithText(string path)
{
    GlobalFontSettings.FontResolver ??= new PlexResolver(fonts);
    using var book = PdfReader.Open(bookPath, PdfDocumentOpenMode.Modify);
    var font = new XFont("Plex", 8);
    foreach (var page in book.Pages)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        gfx.DrawString($"{Name} — {Mobile} — {Reference}", font, XBrushes.Gray,
            new XRect(0, page.Height.Point - 22, page.Width.Point, 12), XStringFormats.Center);
    }

    book.Save(path);
    return path;
}

sealed class PlexResolver(string dir) : IFontResolver
{
    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new("Plex");

    public byte[] GetFont(string faceName) => File.ReadAllBytes(Path.Combine(dir, "IBMPlexSansArabic-Regular.ttf"));
}
