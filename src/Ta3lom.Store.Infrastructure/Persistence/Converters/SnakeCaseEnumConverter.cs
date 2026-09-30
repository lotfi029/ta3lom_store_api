using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ta3lom.Store.Infrastructure.Persistence.Converters;

/// Enum ↔ the DDL's text value: `PendingPayment` ↔ "pending_payment" (database/01 §2).
/// `[EnumMember(Value = …)]` overrides the name where snake_case cannot produce it ("applepay", "INV").
internal sealed class SnakeCaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(v => ToWire[v], v => FromWire[v])
    where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> ToWire = Enum.GetValues<TEnum>().ToDictionary(v => v, Wire);

    private static readonly Dictionary<string, TEnum> FromWire = ToWire.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    public static string Wire(TEnum value)
    {
        var name = value.ToString();
        var member = typeof(TEnum).GetField(name)?.GetCustomAttribute<EnumMemberAttribute>();
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
