using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Security;

/// HMAC-SHA256 with the hashing key, lower-case hex (64 characters). Each kind of value has its own input prefix, so an IP
/// hash can never collide with a mobile or e-mail hash.
internal sealed class Pseudonymizer(IOptions<HashingOptions> options) : IPseudonymizer
{
    private readonly byte[] _key = Convert.FromBase64String(options.Value.KeyBase64);

    public string HashIp(string ip) => Hex("ip:" + ip);

    public string HashMobile(string mobileE164) => Hex("mobile:" + mobileE164);

    public string HashEmail(string email) => Hex("email:" + (email ?? string.Empty).Trim().ToLowerInvariant());

    private string Hex(string value) => Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value)));
}
