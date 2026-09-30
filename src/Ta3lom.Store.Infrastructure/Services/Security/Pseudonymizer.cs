using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Security;

/// HMAC-SHA256 with the PII hash key (09 §5.13). IPs use a separate input prefix so an IP hash can never
/// collide with a mobile hash.
internal sealed class Pseudonymizer(IOptions<PiiOptions> options) : IPseudonymizer
{
    private readonly byte[] _key = Convert.FromBase64String(options.Value.MobileHashKeyBase64);

    public string HashIp(string ip) => Hex("ip:" + ip);

    public string HashMobile(string mobileE164) => Hex(mobileE164);

    private string Hex(string value) => Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value)));
}
