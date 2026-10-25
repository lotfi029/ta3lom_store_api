using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Security;

/// 32 random bytes as base64url in the cookie; SHA-256 lower-case hex in `refresh_token.token_hash`.
internal sealed class RefreshTokenCodec : IRefreshTokenCodec
{
    public (string Raw, string Hash) Create()
    {
        var raw = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (raw, Hash(raw));
    }

    public string Hash(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
