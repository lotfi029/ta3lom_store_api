using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Security;

/// RUL-41: token = base64url(grant id ‖ customer id ‖ expiry unix seconds) + "." + base64url(HMAC-SHA256[..16]) with
/// `Store:Downloads:LinkKeyBase64`; constant-time check, expiry enforced by the caller's clock.
internal sealed class DownloadLinks(IOptions<DownloadKeyOptions> options) : IDownloadLinks
{
    public string Create(Guid grantId, Guid customerId, DateTimeOffset expiresAt)
    {
        var payload = new byte[40];
        grantId.TryWriteBytes(payload.AsSpan(0, 16));
        customerId.TryWriteBytes(payload.AsSpan(16, 16));
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(32, 8), expiresAt.ToUnixTimeSeconds());
        return Base64Url(payload) + "." + Base64Url(Mac(payload));
    }

    public DownloadToken? Read(string token, DateTimeOffset now)
    {
        var parts = (token ?? string.Empty).Split('.');
        if (parts.Length != 2 || !TryDecode(parts[0], out var payload) || payload.Length != 40 || !TryDecode(parts[1], out var mac)
            || !CryptographicOperations.FixedTimeEquals(mac, Mac(payload)))
        {
            return null;
        }

        var expires = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(32, 8)));
        return expires <= now ? null : new DownloadToken(new Guid(payload.AsSpan(0, 16)), new Guid(payload.AsSpan(16, 16)), expires);
    }

    private byte[] Mac(byte[] payload) => HMACSHA256.HashData(Convert.FromBase64String(options.Value.LinkKeyBase64), payload)[..16];

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryDecode(string text, out byte[] bytes)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=');
        bytes = new byte[s.Length];
        return Convert.TryFromBase64String(s, bytes, out var written) && (bytes = bytes[..written]).Length > 0;
    }
}
