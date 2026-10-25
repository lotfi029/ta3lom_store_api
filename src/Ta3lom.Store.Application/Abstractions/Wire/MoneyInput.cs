using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ta3lom.Store.Application.Abstractions.Wire;

/// Wire money (D-02) for every money field the API reads (cart, project and option amounts, settings): a string of ASCII
/// digits with at most two decimals, "50" or "50.00", never a JSON number. Arabic-Indic (U+0660–0669) and Eastern Arabic
/// (U+06F0–06F9) digits and the Arabic decimal separator (U+066B) are mapped to ASCII first; group separators (`,` and
/// U+066C) and anything else are invalid. Parsing never throws on input that passed <see cref="TryParse"/>.
public static partial class MoneyInput
{
    [GeneratedRegex(@"^[0-9]{1,10}(\.[0-9]{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public const decimal Max = 9_999_999_999.99m;

    /// Maps Arabic digits and the Arabic decimal separator to ASCII; every other character is left as it is.
    public static string? Normalize(string? wire)
    {
        if (wire is null)
        {
            return null;
        }

        var builder = new StringBuilder(wire.Length);
        foreach (var c in wire)
        {
            builder.Append(c switch
            {
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                '٫' => '.',
                _ => c,
            });
        }

        return builder.ToString();
    }

    public static bool TryParse(string? wire, out Money money)
    {
        money = Money.Zero;
        var normalized = Normalize(wire);
        if (normalized is null || !Pattern().IsMatch(normalized)
            || !decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value > Max)
        {
            return false;
        }

        money = Money.Round(value);
        return true;
    }

    public static bool IsValid(string? wire) => TryParse(wire, out _);

    /// A valid amount greater than zero.
    public static bool IsPositive(string? wire) => TryParse(wire, out var money) && money.IsPositive;

    /// For input that passed validation; a malformed value is a bug in the caller.
    public static Money Parse(string wire) =>
        TryParse(wire, out var money) ? money : throw new FormatException("Not a wire money value.");
}
