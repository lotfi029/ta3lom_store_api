using System.Globalization;
using System.Text;

namespace Ta3lom.Store.Domain.Services;

/// ZATCA Phase 1 QR for a simplified tax invoice (09 §9.3): TLV tags 1 seller name, 2 VAT number, 3 timestamp (ISO 8601
/// UTC), 4 total incl. VAT, 5 VAT total — each `tag, length, UTF-8 value`, the whole base64-encoded.
public static class ZatcaQr
{
    public static string Encode(string sellerName, string vatNumber, DateTimeOffset issuedAt, Money total, Money vat)
    {
        using var ms = new MemoryStream();
        Write(ms, 1, sellerName);
        Write(ms, 2, vatNumber);
        Write(ms, 3, issuedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        Write(ms, 4, total.ToWire());
        Write(ms, 5, vat.ToWire());
        return Convert.ToBase64String(ms.ToArray());
    }

    /// Reads the five values back (tests, support).
    public static IReadOnlyDictionary<int, string> Decode(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        var values = new Dictionary<int, string>();
        for (var i = 0; i + 1 < bytes.Length;)
        {
            int tag = bytes[i], length = bytes[i + 1];
            values[tag] = Encoding.UTF8.GetString(bytes, i + 2, length);
            i += 2 + length;
        }

        return values;
    }

    private static void Write(MemoryStream ms, byte tag, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Guard.Against(bytes.Length > 255, "zatca.tlv_value_too_long");
        ms.WriteByte(tag);
        ms.WriteByte((byte)bytes.Length);
        ms.Write(bytes);
    }
}
