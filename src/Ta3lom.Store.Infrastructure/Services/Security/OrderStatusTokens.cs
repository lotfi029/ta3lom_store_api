using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Security;

/// 11 §6.3: `st_` + base64url(HMAC-SHA256(Store:Tokens:StatusKeyBase64, order id))[..16 bytes]; stateless, constant-time check.
internal sealed class OrderStatusTokens(IOptions<TokenKeyOptions> options) : IOrderStatusTokens
{
    private const string Prefix = "st_";

    public string Issue(Guid orderId) => Prefix + Base64Url(Mac(orderId));

    public bool Verify(Guid orderId, string? token)
    {
        if (token is null || !token.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var expected = System.Text.Encoding.ASCII.GetBytes(Issue(orderId));
        var actual = System.Text.Encoding.ASCII.GetBytes(token);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private byte[] Mac(Guid orderId) =>
        HMACSHA256.HashData(Convert.FromBase64String(options.Value.StatusKeyBase64), orderId.ToByteArray())[..16];

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
