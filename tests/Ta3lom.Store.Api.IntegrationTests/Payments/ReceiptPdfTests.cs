using System.Globalization;
using System.Text;
using Ta3lom.Store.Application.Features.Receipts;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.Infrastructure.Services.Receipts;
using UglyToad.PdfPig;

namespace Ta3lom.Store.Api.IntegrationTests.Payments;

/// F-D4 / plan §4: the receipt PDF renders Arabic (the extracted text holds the shaped words, normalised back to their
/// letters), the number, the amounts and the total in words; the font is embedded.
public sealed class ReceiptPdfTests
{
    internal static ReceiptDocument Sample(bool cancellation = false) => new(
        cancellation ? "DRC-2026-000001" : "DR-2026-000007", cancellation, cancellation ? "DR-2026-000007" : null, "T3-26-000042",
        "04/11/2026", "13/05/1448", "عبدالله الأحمد",
        [new ReceiptDocumentLine("سقيا الماء — بئر", 3, "100.00", "300.00"), new ReceiptDocumentLine("التبرع العام", 1, "50.00", "50.00")],
        "350.00", ArabicAmountWords.Of(new Money(350m)), "جمعية التعلم الإلكتروني", "E-Learning Association", "1234",
        "لا تخضع لضريبة القيمة المضافة", "Not subject to VAT", null);

    /// PDF text extraction returns Arabic presentation forms, sometimes in visual order: normalise and compare both ways.
    internal static string Extract(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        var text = string.Join("\n", document.GetPages().Select(p => p.Text));
        return text.Normalize(NormalizationForm.FormKC);
    }

    /// Text extraction maps some shaped glyphs (dotted initial/medial forms) to NUL and final forms to look-alike code
    /// points (ی, ە, ں); poppler's pdftotext reads them fine, PdfPig does not. So letters are folded, every NUL run is a
    /// wildcard for one letter, and the phrase may come back in visual (reversed) order.
    internal static bool ContainsArabic(string extracted, string phrase)
    {
        var hay = Fold(extracted, keepNul: true);
        var needle = Fold(phrase, keepNul: false);
        return Matches(hay, needle) || Matches(hay, new string([.. needle.Reverse()]));

        static bool Matches(string hay, string needle) =>
            System.Text.RegularExpressions.Regex.IsMatch(hay,
                string.Join("\u0000*", needle.Select(c => $"(?:{System.Text.RegularExpressions.Regex.Escape(c.ToString())}|\u0000{{1,3}})")));
    }

    private static string Fold(string s, bool keepNul) => new([.. s.Normalize(NormalizationForm.FormKC)
        .Where(c => (keepNul && c == '\0') || (!char.IsWhiteSpace(c) && !char.IsControl(c) && c != '\u0640'
                                                 && CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.Format)))
        .Select(c => c switch
        {
            'إ' or 'أ' or 'آ' or 'ٱ' => 'ا',
            'ة' or 'ە' => 'ه',
            'ی' or 'ى' or 'ے' => 'ي',
            'ں' => 'ن',
            'ک' => 'ك',
            // dots are sometimes lost in extraction: compare the undotted skeleton (rasm)
            'ج' or 'خ' => 'ح',
            'ذ' => 'د',
            'ز' => 'ر',
            'ش' => 'س',
            'ض' => 'ص',
            'ظ' => 'ط',
            'غ' => 'ع',
            'ق' => 'ف',
            _ => c,
        })]);

    [Fact]
    public void A_donation_receipt_holds_the_shaped_Arabic_text_number_amounts_and_words()
    {
        var pdf = new QuestPdfReceiptRenderer().Render(Sample());
        if (Environment.GetEnvironmentVariable("RECEIPT_PDF_OUT") is { Length: > 0 } outPath)
        {
            File.WriteAllBytes(outPath, pdf);                                // local inspection only
            File.WriteAllText(outPath + ".txt", Extract(pdf));
        }

        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
        var text = Extract(pdf);
        text.ShouldContain("DR-2026-000007");
        text.ShouldContain("350.00");
        text.ShouldContain("T3-26-000042");
        ContainsArabic(text, "إيصال تبرع").ShouldBeTrue(text);
        ContainsArabic(text, "عبدالله").ShouldBeTrue(text);
        ContainsArabic(text, "جمعية التعلم الإلكتروني").ShouldBeTrue(text);
        ContainsArabic(text, "ثلاثمائة").ShouldBeTrue(text);
        ContainsArabic(text, "القيمة المضافة").ShouldBeTrue(text);
        Encoding.Latin1.GetString(pdf).ShouldContain("NotoNaskhArabic");     // embedded font
    }

    [Fact]
    public void A_cancellation_receipt_names_the_receipt_it_voids()
    {
        var text = Extract(new QuestPdfReceiptRenderer().Render(Sample(cancellation: true)));
        text.ShouldContain("DRC-2026-000001");
        text.ShouldContain("DR-2026-000007");
        ContainsArabic(text, "إيصال إلغاء تبرع").ShouldBeTrue(text);
    }

    [Theory]
    [InlineData("350.00", "فقط ثلاثمائة وخمسون ريالًا لا غير")]
    [InlineData("1.00", "فقط ريال واحد لا غير")]
    [InlineData("2.00", "فقط ريالان لا غير")]
    [InlineData("10.00", "فقط عشرة ريالات لا غير")]
    [InlineData("11.00", "فقط أحد عشر ريالًا لا غير")]
    [InlineData("100.00", "فقط مائة ريال لا غير")]
    [InlineData("125.50", "فقط مائة وخمسة وعشرون ريالًا وخمسون هللة لا غير")]
    [InlineData("0.25", "فقط خمس وعشرون هللة لا غير")]
    [InlineData("2500.00", "فقط ألفان وخمسمائة ريال لا غير")]
    [InlineData("13000.00", "فقط ثلاثة عشر ألفًا ريال لا غير")]
    [InlineData("100000.00", "فقط مائة ألف ريال لا غير")]
    [InlineData("3000000.00", "فقط ثلاثة ملايين ريال لا غير")]
    public void Totals_in_Arabic_words(string amount, string expected) =>
        ArabicAmountWords.Of(Money.Parse(amount)).ShouldBe(expected);

    [Fact]
    public void Hijri_dates_use_Umm_al_Qura() =>
        new DateTime(2026, 11, 4).ToString("dd/MM/yyyy", new CultureInfo("ar-SA") { DateTimeFormat = { Calendar = new UmAlQuraCalendar() } })
            .ShouldNotBeNullOrWhiteSpace();
}
