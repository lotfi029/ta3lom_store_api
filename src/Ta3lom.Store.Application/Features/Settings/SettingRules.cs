using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Application.Features.Settings;

/// Typed validation per key (11 §9.17, RUL §M): money settings are strings, counts and percentages integers.
internal static partial class SettingRules
{
    private static readonly string[] BoolKeys = [SettingKeys.VatEnabled, SettingKeys.DedicationFreeTextEnabled];

    private static readonly string[] MoneyKeys = [SettingKeys.ShippingFlatFee, SettingKeys.MinGiftAmount, SettingKeys.CartMaxTotal];

    private static readonly Dictionary<string, (int Min, int Max)> IntKeys = new(StringComparer.Ordinal)
    {
        [SettingKeys.OrderPaymentTimeoutMin] = (5, 1440),
        [SettingKeys.DownloadLimit] = (1, 100),
        [SettingKeys.AllocationAlertDays] = (1, 365),
        [SettingKeys.AllocationEscalateDays] = (1, 730),
        [SettingKeys.LapseDays] = (1, 365),
        [SettingKeys.RefundWindowDays] = (0, 365),
        [SettingKeys.RefundProgressMaxPct] = (0, 100),
        [SettingKeys.LowStockDefaultThreshold] = (0, 100_000),
        [SettingKeys.GiftRefundWindowHours] = (0, 720),
    };

    [GeneratedRegex(@"^\d{1,10}\.\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex MoneyString();

    [GeneratedRegex("^3[0-9]{13}3$", RegexOptions.CultureInvariant)]
    private static partial Regex VatNumber();

    /// The wire value of a stored setting: money keys are always `"25.00"` strings (11 §9.17), whatever the seed stored.
    public static JsonElement ToWire(string key, string storedJson)
    {
        using var doc = JsonDocument.Parse(storedJson);
        var value = doc.RootElement;
        if (MoneyKeys.Contains(key) && value.ValueKind == JsonValueKind.Number)
        {
            var text = Math.Round(value.GetDecimal(), 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
            return JsonSerializer.SerializeToElement(text);
        }

        return value.Clone();
    }

    public static bool IsKnown(string key) => SettingKeys.All.Contains(key, StringComparer.Ordinal);

    public static bool IsValid(string key, JsonElement value)
    {
        if (BoolKeys.Contains(key))
        {
            return value.ValueKind is JsonValueKind.True or JsonValueKind.False;
        }

        if (MoneyKeys.Contains(key))
        {
            return value.ValueKind == JsonValueKind.String && MoneyString().IsMatch(value.GetString()!)
                   && decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture) >= 0;
        }

        if (IntKeys.TryGetValue(key, out var range))
        {
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n >= range.Min && n <= range.Max;
        }

        return key switch
        {
            SettingKeys.VatRate => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var rate) && rate is >= 0 and <= 100,
            SettingKeys.SellerVatNo => value.ValueKind == JsonValueKind.Null || (value.ValueKind == JsonValueKind.String && VatNumber().IsMatch(value.GetString()!)),
            SettingKeys.DedicationTemplates => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= 20 && value.EnumerateArray().All(t =>
                t.ValueKind == JsonValueKind.Object
                && t.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String && k.GetString()!.Length is > 0 and <= 40
                && t.TryGetProperty("ar", out var ar) && ar.ValueKind == JsonValueKind.String && ar.GetString()!.Length is > 0 and <= 120
                && (!t.TryGetProperty("en", out var en) || en.ValueKind is JsonValueKind.String or JsonValueKind.Null)),
            _ => false,
        };
    }
}
