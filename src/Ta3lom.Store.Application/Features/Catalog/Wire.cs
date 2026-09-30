using System.Globalization;

namespace Ta3lom.Store.Application.Features.Catalog;

/// JSON money strings (RUL-31, 11 §1.3): always two decimals, invariant culture.
internal static class Wire
{
    public static string Money(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    public static string? Money(decimal? amount) => amount is null ? null : Money(amount.Value);

    public static string Money(Money amount) => amount.ToWire();

    public static string? Money(Money? amount) => amount?.ToWire();
}
