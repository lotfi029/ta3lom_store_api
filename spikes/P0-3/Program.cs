// P0-3 — can QuestPDF render the store's Arabic invoice and donation receipt with IBM Plex Sans Arabic:
// shaping, RTL paragraphs with LTR numbers and references, a table, and the ZATCA TLV QR code?
using System.Diagnostics;
using System.Globalization;
using System.Text;
using QRCoder;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;           // association (non-profit) → Community licence
QuestPDF.Settings.EnableDetailedLayoutErrors = false;

var fonts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Ta3lom.Store.Infrastructure/Resources/Fonts"));
foreach (var file in Directory.GetFiles(fonts, "*.ttf"))
{
    using var stream = File.OpenRead(file);
    FontManager.RegisterFontFromStream(stream);
}

var outDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/spikes/P0-3"));
Directory.CreateDirectory(outDir);
var ar = CultureInfo.GetCultureInfo("ar-SA");
const string Primary = "IBM Plex Sans Arabic";
const string Fallback = "Noto Naskh Arabic";

var sw = Stopwatch.StartNew();
var invoicePath = Path.Combine(outDir, "invoice-INV-2026-000412.pdf");
Invoice().GeneratePdf(invoicePath);
var invoiceMs = sw.ElapsedMilliseconds;
sw.Restart();
var receiptPath = Path.Combine(outDir, "receipt-DR-2026-000187.pdf");
Receipt().GeneratePdf(receiptPath);
var receiptMs = sw.ElapsedMilliseconds;
sw.Restart();
for (var i = 0; i < 20; i++)
{
    Invoice().GeneratePdf();
}

Console.WriteLine($"invoice {new FileInfo(invoicePath).Length} bytes, first {invoiceMs} ms");
Console.WriteLine($"receipt {new FileInfo(receiptPath).Length} bytes, first {receiptMs} ms");
Console.WriteLine($"20 warm invoices: {sw.ElapsedMilliseconds} ms ({sw.ElapsedMilliseconds / 20.0:0.0} ms each)");
Console.WriteLine($"QR TLV: {ZatcaTlv("جمعية التعلم الإلكتروني", "300000000000003", new DateTimeOffset(2026, 9, 14, 11, 2, 0, TimeSpan.Zero), "480.00", "62.61")}");

Document Invoice() => Document.Create(doc => doc.Page(page =>
{
    Page(page);
    page.Header().Element(h => Header(h, "فاتورة ضريبية مبسطة", "Simplified tax invoice", "INV-2026-000412"));
    page.Content().PaddingVertical(10).Column(col =>
    {
        col.Spacing(8);
        col.Item().Text(t =>
        {
            t.Span("رقم الطلب: ").SemiBold();
            t.Span("T3-26-004187");                                                   // LTR run inside RTL text
            t.Span("  •  التاريخ: ").SemiBold();
            t.Span(new DateTime(2026, 9, 14).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));
        });
        col.Item().Text("العميل: نورة العتيبي — \u2066+966 55 123 4567\u2069 — \u2066noura@example.com\u2069");
        col.Item().Text("العنوان: بريدة، حي الريان، طريق الملك عبدالعزيز 8123، QBRA2929، الرمز البريدي 52361");
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(28);
                c.RelativeColumn(5);
                c.RelativeColumn(1);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
            });
            table.Header(h =>
            {
                foreach (var title in new[] { "#", "البند", "الكمية", "سعر الوحدة", "الضريبة 15٪", "المبلغ" })
                {
                    h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(title).SemiBold();
                }
            });
            Row(table, "1", "المعلّم الرقمي: أدوات التعليم الإلكتروني", "1", "350.00", "45.65", "350.00");
            Row(table, "2", "نجوم العلوم — كتاب التجارب المنزلية (مطبوع)", "2", "65.00", "16.96", "130.00");
            Row(table, "3", "الشحن داخل المملكة", "1", "25.00", "3.26", "25.00");
        });
        col.Item().AlignLeft().Width(220).Column(t =>
        {
            Total(t, "الإجمالي قبل الضريبة", "439.13");
            Total(t, "ضريبة القيمة المضافة (15٪)", "65.87");
            Total(t, "الإجمالي شامل الضريبة", "505.00 ر.س", bold: true);
        });
        col.Item().Row(r =>
        {
            r.RelativeItem().Column(c =>
            {
                c.Item().Text("الرقم الضريبي للبائع: 300000000000003");
                c.Item().Text("الأسعار بالريال السعودي وتشمل ضريبة القيمة المضافة.").FontSize(9).FontColor(Colors.Grey.Darken1);
                c.Item().Text("\u2066English line: Invoice INV-2026-000412 for order T3-26-004187.\u2069").FontSize(9);
            });
            r.ConstantItem(96).Image(QrPng(ZatcaTlv("جمعية التعلم الإلكتروني", "300000000000003",
                new DateTimeOffset(2026, 9, 14, 11, 2, 0, TimeSpan.Zero), "505.00", "65.87")));
        });
    });
    Footer(page);
}));

Document Receipt() => Document.Create(doc => doc.Page(page =>
{
    Page(page);
    page.Header().Element(h => Header(h, "إيصال تبرع", "Donation receipt", "DR-2026-000187"));
    page.Content().PaddingVertical(10).Column(col =>
    {
        col.Spacing(8);
        col.Item().Text("تشكر جمعية التعلم الإلكتروني المتبرع الكريم على عطائه.").FontSize(12);
        col.Item().Text(t =>
        {
            t.Span("اسم المتبرع: ").SemiBold();
            t.Span("عبدالرحمن بن سعيد آل مُحمّد");                                        // tashkeel + long name
        });
        col.Item().Text("طريقة الإهداء: فاعل خير (يظهر الاسم الحقيقي في الإيصال فقط)");
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(5);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
            });
            table.Header(h =>
            {
                foreach (var title in new[] { "المشروع", "النية", "المبلغ" })
                {
                    h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(title).SemiBold();
                }
            });
            foreach (var (p, i, a) in new[] { ("مهارات المذاكرة الذكية — 5 مقاعد", "صدقة", "600.00"), ("سقيا الطلاب — برّاد مياه", "زكاة", "1,500.00") })
            {
                table.Cell().BorderBottom(0.5f).Padding(4).Text(p);
                table.Cell().BorderBottom(0.5f).Padding(4).Text(i);
                table.Cell().BorderBottom(0.5f).Padding(4).AlignLeft().Text(a);
            }
        });
        col.Item().Text(t =>
        {
            t.Span("الإجمالي: ").SemiBold();
            t.Span("2,100.00 ر.س").SemiBold();
            t.Span("  —  منها زكاة ");
            t.Span(1500m.ToString("N2", ar));                                         // Arabic-Indic digits (٬ ٫) for comparison
        });
        col.Item().Text("هذا الإيصال ليس فاتورة ضريبية. التبرعات خارج نطاق ضريبة القيمة المضافة.").FontSize(9).FontColor(Colors.Grey.Darken1);
        col.Item().Text("نص بخط النسخ الاحتياطي للمقارنة").FontFamily(Fallback);
    });
    Footer(page);
}));

void Page(PageDescriptor page)
{
    page.Size(PageSizes.A4);
    page.Margin(36);
    page.ContentFromRightToLeft();
    page.DefaultTextStyle(s => s.FontFamily(Primary, Fallback).FontSize(10.5f));
}

void Header(IContainer h, string titleAr, string titleEn, string number) => h.BorderBottom(1).PaddingBottom(6).Row(r =>
{
    r.RelativeItem().Column(c =>
    {
        c.Item().Text("جمعية التعلم الإلكتروني — متجر تعلّم").Bold().FontSize(14);
        c.Item().Text("الرياض، المملكة العربية السعودية").FontSize(9);
    });
    r.RelativeItem().AlignLeft().Column(c =>
    {
        c.Item().AlignLeft().Text(titleAr).Bold().FontSize(16);
        c.Item().AlignLeft().Text(titleEn).FontSize(9);
        c.Item().AlignLeft().Text(number).FontSize(11);
    });
});

void Footer(PageDescriptor page) => page.Footer().AlignCenter().Text(t =>
{
    t.Span("صفحة ");
    t.CurrentPageNumber();
    t.Span(" من ");
    t.TotalPages();
});

void Row(TableDescriptor t, params string[] cells)
{
    for (var i = 0; i < cells.Length; i++)
    {
        var cell = t.Cell().BorderBottom(0.5f).Padding(4);
        (i >= 2 ? cell.AlignLeft() : cell).Text(cells[i]);
    }
}

void Total(ColumnDescriptor c, string label, string value, bool bold = false) => c.Item().Row(r =>
{
    var l = r.RelativeItem().Text(label);
    var v = r.AutoItem().Text(value);
    if (bold)
    {
        l.Bold();
        v.Bold();
    }
});

// ZATCA phase-1 QR: TLV (tag, length, UTF-8 value) for seller, VAT no, timestamp, total, VAT → base64.
static string ZatcaTlv(string seller, string vatNo, DateTimeOffset at, string total, string vat)
{
    using var ms = new MemoryStream();
    void Write(byte tag, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        ms.WriteByte(tag);
        ms.WriteByte((byte)bytes.Length);
        ms.Write(bytes);
    }

    Write(1, seller);
    Write(2, vatNo);
    Write(3, at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
    Write(4, total);
    Write(5, vat);
    return Convert.ToBase64String(ms.ToArray());
}

static byte[] QrPng(string payload)
{
    using var generator = new QRCodeGenerator();
    using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
    return new PngByteQRCode(data).GetGraphic(8);
}
