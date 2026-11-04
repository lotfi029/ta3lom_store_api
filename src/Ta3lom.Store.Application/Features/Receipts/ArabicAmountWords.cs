using System.Text;

namespace Ta3lom.Store.Application.Features.Receipts;

/// The receipt total in Arabic words (plan §4: "total in figures and words"), Saudi receipt style (review 04 S-17):
/// `فقط ثلاثمائة وخمسون ريال سعودي وخمس وعشرون هللة لا غير`. Covers 0 … 999,999,999.99. After a number the currency is the
/// fixed «ريال سعودي» / «هللة» of receipts and cheques; one and two put the noun first («ريال سعودي واحد», «ريالان
/// سعوديان», «هللة واحدة», «هللتان»). Thousands and millions agree with their own count (ألف, ألفان, 3–10 آلاف/ملايين,
/// otherwise ألف/مليون without tanween: «ثلاثة عشر ألف ريال سعودي»).
public static class ArabicAmountWords
{
    private static readonly string[] Units =
    [
        "", "واحد", "اثنان", "ثلاثة", "أربعة", "خمسة", "ستة", "سبعة", "ثمانية", "تسعة", "عشرة",
        "أحد عشر", "اثنا عشر", "ثلاثة عشر", "أربعة عشر", "خمسة عشر", "ستة عشر", "سبعة عشر", "ثمانية عشر", "تسعة عشر",
    ];

    private static readonly string[] Tens = ["", "", "عشرون", "ثلاثون", "أربعون", "خمسون", "ستون", "سبعون", "ثمانون", "تسعون"];

    private static readonly string[] Hundreds = ["", "مائة", "مائتان", "ثلاثمائة", "أربعمائة", "خمسمائة", "ستمائة", "سبعمائة", "ثمانمائة", "تسعمائة"];

    public static string Of(Money amount)
    {
        var halalas = amount.ToHalalas();
        if (halalas < 0 || halalas > 99_999_999_999L)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Out of the receipt range.");
        }

        var riyals = halalas / 100;
        var rest = (int)(halalas % 100);
        var sb = new StringBuilder("فقط ");
        sb.Append(riyals == 0 && rest > 0 ? string.Empty : Counted(riyals, "ريال سعودي", "ريال سعودي واحد", "ريالان سعوديان"));
        if (rest > 0)
        {
            sb.Append(riyals == 0 ? string.Empty : " و").Append(Counted(rest, "هللة", "هللة واحدة", "هللتان", feminine: true));
        }

        return sb.Append(" لا غير").ToString();
    }

    /// The number in words followed by the currency (the noun alone for 1 and 2: "ريال سعودي واحد", "ريالان سعوديان").
    private static string Counted(long n, string noun, string one, string two, bool feminine = false) => n switch
    {
        0 => $"صفر {noun}",
        1 => one,
        2 => two,
        _ => $"{Words(n, feminine)} {noun}",
    };

    public static string Words(long n, bool feminine = false)
    {
        if (n == 0)
        {
            return "صفر";
        }

        List<string> parts = [];
        var millions = n / 1_000_000;
        var thousands = n / 1000 % 1000;
        var rest = (int)(n % 1000);
        if (millions > 0)
        {
            parts.Add(Scale(millions, "مليون", "مليونان", "ملايين"));
        }

        if (thousands > 0)
        {
            parts.Add(Scale(thousands, "ألف", "ألفان", "آلاف"));
        }

        if (rest > 0)
        {
            parts.Add(Below1000(rest, feminine));
        }

        return string.Join(" و", parts);
    }

    private static string Scale(long n, string one, string two, string few) => n switch
    {
        1 => one,
        2 => two,
        >= 3 and <= 10 => $"{Below1000((int)n, false)} {few}",
        _ => $"{Below1000((int)n, false)} {one}",
    };

    private static string Below1000(int n, bool feminine)
    {
        List<string> parts = [];
        if (n >= 100)
        {
            parts.Add(Hundreds[n / 100]);
        }

        var r = n % 100;
        if (r > 0)
        {
            parts.Add(Below100(r, feminine));
        }

        return string.Join(" و", parts);
    }

    private static string Below100(int n, bool feminine)
    {
        if (n < 20)
        {
            return feminine ? Feminine(n) : Units[n];
        }

        var unit = n % 10;
        return unit == 0 ? Tens[n / 10] : $"{(feminine ? Feminine(unit) : Units[unit])} و{Tens[n / 10]}";
    }

    /// Feminine forms for هللة (3–10 take the masculine-looking form without ة).
    private static string Feminine(int n) => n switch
    {
        1 => "واحدة",
        2 => "اثنتان",
        3 => "ثلاث",
        4 => "أربع",
        5 => "خمس",
        6 => "ست",
        7 => "سبع",
        8 => "ثماني",
        9 => "تسع",
        10 => "عشر",
        11 => "إحدى عشرة",
        12 => "اثنتا عشرة",
        _ => $"{Feminine(n - 10)} عشرة",
    };
}
