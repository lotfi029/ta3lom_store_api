using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace Ta3lom.Store.Domain.Enums;

/// Enum ↔ the DDL/API text value: `PendingPayment` ↔ "pending_payment" (database/01 §2, 11 §1).
/// `[EnumMember(Value = …)]` overrides the name where snake_case cannot produce it ("applepay", "INV").
/// Used by the EF converter and by the Mapster configs, so the database and the JSON always agree.
public static class EnumWire
{
    private static readonly ConcurrentDictionary<Type, (Dictionary<Enum, string> ToWire, Dictionary<string, Enum> FromWire)> Maps = new();

    public static string ToWire<TEnum>(this TEnum value)
        where TEnum : struct, Enum => Map(typeof(TEnum)).ToWire[value];

    public static TEnum Parse<TEnum>(string wire)
        where TEnum : struct, Enum => (TEnum)Map(typeof(TEnum)).FromWire[wire];

    public static bool TryParse<TEnum>(string? wire, out TEnum value)
        where TEnum : struct, Enum
    {
        if (wire is not null && Map(typeof(TEnum)).FromWire.TryGetValue(wire, out var found))
        {
            value = (TEnum)found;
            return true;
        }

        value = default;
        return false;
    }

    private static (Dictionary<Enum, string> ToWire, Dictionary<string, Enum> FromWire) Map(Type type) =>
        Maps.GetOrAdd(type, t =>
        {
            var to = Enum.GetValues(t).Cast<Enum>().ToDictionary(v => v, v => Wire(t, v));
            return (to, to.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal));
        });

    private static string Wire(Type type, Enum value)
    {
        var name = value.ToString();
        var member = type.GetField(name)?.GetCustomAttribute<EnumMemberAttribute>();
        return member?.Value ?? SnakeCase(name);
    }

    private static string SnakeCase(string pascal)
    {
        var sb = new StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            if (char.IsUpper(pascal[i]) && i > 0)
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(pascal[i]));
        }

        return sb.ToString();
    }
}
