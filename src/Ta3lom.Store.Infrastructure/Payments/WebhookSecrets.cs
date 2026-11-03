using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ta3lom.Store.Infrastructure.Payments;

/// The Moyasar webhook scheme (shared `secret_token` in the JSON body), shared by the fake: constant-time comparison and
/// a redacted copy of the payload for the inbox (no secret, no card details).
internal static class WebhookSecrets
{
    /// Constant time whatever the lengths: both sides are hashed first.
    public static bool Matches(string? presented, string expected) =>
        presented is not null && expected.Length > 0
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(presented)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    /// Keeps only what an operator needs: ids, type, mode, and the payment's id, status, amounts, currency, invoice id,
    /// source type/company/message. Everything else (secret, masked card, issuer, IP, metadata) is dropped.
    public static string Redact(JsonObject root)
    {
        var data = root["data"] as JsonObject;
        var source = data?["source"] as JsonObject;
        var kept = new JsonObject
        {
            ["id"] = root["id"]?.DeepClone(),
            ["type"] = root["type"]?.DeepClone(),
            ["created_at"] = root["created_at"]?.DeepClone(),
            ["live"] = root["live"]?.DeepClone(),
        };
        if (data is not null)
        {
            var d = new JsonObject();
            foreach (var key in new[] { "id", "status", "amount", "captured", "refunded", "currency", "invoice_id", "created_at", "updated_at" })
            {
                if (data[key] is { } value)
                {
                    d[key] = value.DeepClone();
                }
            }

            if (source is not null)
            {
                d["source"] = new JsonObject { ["type"] = source["type"]?.DeepClone(), ["company"] = source["company"]?.DeepClone(), ["message"] = source["message"]?.DeepClone() };
            }

            kept["data"] = d;
        }

        return kept.ToJsonString();
    }

    public static JsonObject? Parse(ReadOnlySpan<byte> raw)
    {
        try
        {
            return JsonNode.Parse(raw) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
