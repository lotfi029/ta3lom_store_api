using System.Globalization;
using System.Text.RegularExpressions;

namespace Ta3lom.Store.Application.Features.AdminCatalog;

/// Wire money (D-02): a string with at most two decimals, "50" or "50.00"; never a JSON number.
public static partial class MoneyInput
{
    [GeneratedRegex(@"^\d{1,10}(\.\d{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValid(string? wire) => wire is not null && Pattern().IsMatch(wire);

    /// A valid positive amount within numeric(12,2).
    public static bool IsPositive(string? wire) =>
        IsValid(wire) && decimal.Parse(wire!, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture) is > 0 and <= 9_999_999_999.99m;

    public static Money Parse(string wire) => Money.Parse(wire);
}
