using System.Globalization;
using System.Text.RegularExpressions;

namespace Ta3lom.Store.Application.Features.AdminCatalog;

/// Parsing helpers for admin inputs: money strings ("180.00") and Asia/Riyadh wall times (RUL-09).
internal static partial class AdminInput
{
    public static readonly TimeSpan RiyadhOffset = TimeSpan.FromHours(3);

    [GeneratedRegex(@"^\d{1,10}(\.\d{1,2})?$", RegexOptions.CultureInvariant)]
    public static partial Regex MoneyPattern();

    [GeneratedRegex("^[a-z0-9-]{2,80}$", RegexOptions.CultureInvariant)]
    public static partial Regex ProductSlug();

    [GeneratedRegex("^[a-z0-9-]{2,60}$", RegexOptions.CultureInvariant)]
    public static partial Regex CategorySlug();

    public static bool IsMoney(string? value) => value is not null && MoneyPattern().IsMatch(value);

    public static bool IsOptionalMoney(string? value) => value is null || IsMoney(value);

    public static Money Money(string value) => Domain.ValueObjects.Money.Round(decimal.Parse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));

    public static Money? OptionalMoney(string? value) => value is null ? null : Money(value);

    public static decimal Decimal(string value) => decimal.Parse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    /// A Riyadh wall time ("2027-01-15T23:59") as a UTC instant (Riyadh is UTC+3 all year; timestamptz takes UTC only).
    public static DateTimeOffset FromRiyadh(DateTime local) =>
        new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), RiyadhOffset).ToUniversalTime();
}
