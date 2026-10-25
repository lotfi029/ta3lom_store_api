using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Security;

/// Token = base64url(HMAC-SHA256(`Store:Tokens:StatusKeyBase64`, reference)); the order keeps SHA-256(token) in hex.
internal sealed class OrderStatusTokens(IOptions<TokenKeyOptions> options) : IOrderStatusTokens
{
    public (string Token, string Hash) Create(string reference)
    {
        var mac = HMACSHA256.HashData(Convert.FromBase64String(options.Value.StatusKeyBase64), Encoding.UTF8.GetBytes(reference));
        var token = Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, HashOf(token));
    }

    public bool Matches(string token, string storedHash) =>
        token.Length is > 0 and <= 100
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(HashOf(token)), Encoding.ASCII.GetBytes(storedHash));

    private static string HashOf(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
