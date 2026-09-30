using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Security;

/// 09 §5.7: HMAC-SHA256(pepper, "{challengeId:N}:{mobile}:{code}") as lower-case hex (`otp_challenge.code_hash`).
/// The challenge id is part of the input, so equal codes never produce equal hashes.
internal sealed class OtpHasher(IOptions<OtpOptions> options) : IOtpHasher
{
    private readonly byte[] _pepper = Convert.FromBase64String(options.Value.PepperBase64);

    public string Hash(Guid challengeId, string mobileE164, string code) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes($"{challengeId:N}:{mobileE164}:{code}")));

    public bool Matches(Guid challengeId, string mobileE164, string codeHash, string code) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(codeHash.Trim()),
            Encoding.ASCII.GetBytes(Hash(challengeId, mobileE164, code ?? string.Empty)));
}
