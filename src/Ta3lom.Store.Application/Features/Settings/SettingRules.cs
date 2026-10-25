using System.Text.Json;
using System.Text.RegularExpressions;
using Ta3lom.Store.Application.Abstractions.Wire;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Application.Features.Settings;

/// Typed validation per key (the allow-list): money settings are `"10.00"` strings, counts are integers.
internal static partial class SettingRules
{
    private static readonly string[] MoneyKeys = [SettingKeys.MinGiftAmount, SettingKeys.MaxLineAmount, SettingKeys.CartMaxTotal];
    private static readonly string[] TextKeys = [SettingKeys.SiteNameAr, SettingKeys.SiteNameEn];

    private static readonly Dictionary<string, (int Min, int Max)> IntKeys = new(StringComparer.Ordinal)
    {
        [SettingKeys.CartMaxLines] = (1, 50),
        [SettingKeys.PaymentWindowMinutes] = (5, 1440),
    };

    /// numeric(12,2) and within the D-03 / D-04 ceiling of 100,000.
    private const decimal MoneyCeiling = 100_000.00m;


    [GeneratedRegex(@"^\+9665[0-9]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex SaudiMobileE164();

    public static bool IsKnown(string key) => SettingKeys.All.Contains(key, StringComparer.Ordinal);

    /// The wire value of a stored setting: money keys are always `"10.00"` strings, whatever the seed stored.
    public static JsonElement ToWire(string key, string storedJson)
    {
        using var doc = JsonDocument.Parse(storedJson);
        var value = doc.RootElement;
        if (MoneyKeys.Contains(key) && value.ValueKind == JsonValueKind.Number)
        {
            return JsonSerializer.SerializeToElement(Money.Round(value.GetDecimal()).ToWire());
        }

        if (key == SettingKeys.AmountPresets && value.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.SerializeToElement(value.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.Number ? Money.Round(e.GetDecimal()).ToWire() : e.GetString()).ToArray());
        }

        return value.Clone();
    }

    public static bool IsValid(string key, JsonElement value)
    {
        if (MoneyKeys.Contains(key))
        {
            return IsMoney(value);
        }

        if (IntKeys.TryGetValue(key, out var range))
        {
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n >= range.Min && n <= range.Max;
        }

        if (TextKeys.Contains(key))
        {
            return value.ValueKind == JsonValueKind.Null || (value.ValueKind == JsonValueKind.String && value.GetString()!.Length is > 0 and <= 120);
        }

        return key switch
        {
            SettingKeys.AmountPresets => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() is >= 1 and <= 6
                                         && value.EnumerateArray().All(IsMoney),
            SettingKeys.SupportMobile => value.ValueKind == JsonValueKind.Null
                                         || (value.ValueKind == JsonValueKind.String && SaudiMobileE164().IsMatch(value.GetString()!)),
            _ => false,
        };
    }

    /// A money value through the shared wire parser (A-3: Arabic digits accepted, nothing ever throws).
    private static bool IsMoney(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && MoneyInput.TryParse(value.GetString(), out var money)
        && money.IsPositive && money.Amount <= MoneyCeiling;

    /// The value as stored: money strings in the canonical `"10.00"` form (ASCII digits, two decimals).
    public static JsonElement Canonical(string key, JsonElement value)
    {
        if (MoneyKeys.Contains(key) && value.ValueKind == JsonValueKind.String)
        {
            return JsonSerializer.SerializeToElement(MoneyInput.Parse(value.GetString()!).ToWire());
        }

        return key == SettingKeys.AmountPresets && value.ValueKind == JsonValueKind.Array
            ? JsonSerializer.SerializeToElement(value.EnumerateArray().Select(e => MoneyInput.Parse(e.GetString()!).ToWire()).ToArray())
            : value.Clone();
    }
}
