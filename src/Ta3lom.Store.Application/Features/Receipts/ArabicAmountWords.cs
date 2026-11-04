using System.Text;

namespace Ta3lom.Store.Application.Features.Receipts;

/// The receipt total in Arabic words (plan §4: "total in figures and words"), Saudi receipt style:
/// `فقط ثلاثمائة وخمسون ريالًا وخمس وعشرون هللة لا غير`. Covers 0 … 999,999,999.99. The counted noun follows the last
/// two digits (1 → واحد after the noun, 2 → dual, 3–10 → plural, 11–99 → singular accusative, 00 → singular).
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
        sb.Append(riyals == 0 && rest > 0 ? string.Empty : Counted(riyals, "ريال", "ريالان", "ريالات", "ريالًا"));
        if (rest > 0)
        {
            sb.Append(riyals == 0 ? string.Empty : " و").Append(Counted(rest, "هللة", "هللتان", "هللات", "هللة", feminine: true));
        }

        return sb.Append(" لا غير").ToString();
    }

    /// The number in words followed by its counted noun (the noun first for 1 and 2: "ريال واحد", "ريالان").
    private static string Counted(long n, string one, string two, string few, string many, bool feminine = false)
    {
        switch (n)
        {
            case 0:
                return $"صفر {one}";
            case 1:
                return $"{one} {(feminine ? "واحدة" : "واحد")}";
            case 2:
                return two;
        }

        var lastTwo = n % 100;
        var noun = lastTwo switch
        {
            >= 3 and <= 10 => few,
            >= 11 and <= 99 => many,
            _ => one,
        };
        return $"{Words(n, feminine)} {noun}";
    }

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
            parts.Add(Scale(millions, "مليون", "مليونان", "ملايين", "مليونًا"));
        }

        if (thousands > 0)
        {
            parts.Add(Scale(thousands, "ألف", "ألفان", "آلاف", "ألفًا"));
        }

        if (rest > 0)
        {
            parts.Add(Below1000(rest, feminine));
        }

        return string.Join(" و", parts);
    }

    private static string Scale(long n, string one, string two, string few, string many) => n switch
    {
        1 => one,
        2 => two,
        >= 3 and <= 10 => $"{Below1000((int)n, false)} {few}",
        _ => $"{Below1000((int)n, false)} {(n % 100 is >= 11 and <= 99 ? many : one)}",
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
