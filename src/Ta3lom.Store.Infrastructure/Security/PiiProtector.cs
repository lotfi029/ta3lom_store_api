using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Ta3lom.Store.Infrastructure.Security;

/// Encrypts beneficiary PII (purpose `Store.Beneficiary.Pii.v1`) and hashes mobiles for lookup (09 §5.13).
public interface IPiiProtector
{
    byte[] Protect(string plain);

    string Unprotect(byte[] cipher);

    /// HMAC-SHA256 of the E.164 mobile, lower-case hex (beneficiary.mobile_hash).
    string MobileHash(string e164);
}

public sealed class PiiOptions
{
    public const string Section = "Store:Pii";

    /// 32 bytes, base64. From /run/secrets in production (09 §7.1).
    public string MobileHashKeyBase64 { get; set; } = string.Empty;
}

internal sealed class PiiProtector(IDataProtectionProvider provider, IOptions<PiiOptions> options) : IPiiProtector
{
    public const string Purpose = "Store.Beneficiary.Pii.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    private readonly byte[] _hashKey = Convert.FromBase64String(options.Value.MobileHashKeyBase64);

    public byte[] Protect(string plain) => _protector.Protect(Encoding.UTF8.GetBytes(plain));

    public string Unprotect(byte[] cipher) => Encoding.UTF8.GetString(_protector.Unprotect(cipher));

    public string MobileHash(string e164) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_hashKey, Encoding.UTF8.GetBytes(e164)));
}
