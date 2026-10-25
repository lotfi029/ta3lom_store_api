using System.Buffers.Binary;
using System.Security.Cryptography;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Security;

/// ASP.NET Core Identity `PasswordHasher` v3 format, written without the Identity package (D-12: PBKDF2-SHA512, 600,000
/// iterations): 0x01 | PRF (HMAC-SHA512 = 2) | iterations | salt length | salt (16 bytes) | subkey (32 bytes), base64.
/// Hashes made with fewer iterations or another PRF still verify and are reported by <see cref="NeedsRehash"/>.
internal sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const int Iterations = 600_000;
    private const int SaltLength = 16;
    private const int SubkeyLength = 32;
    private const uint PrfHmacSha512 = 2;
    private const string OneTimeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var subkey = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA512, SubkeyLength);
        var output = new byte[13 + SaltLength + SubkeyLength];
        output[0] = 0x01;
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(1), PrfHmacSha512);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(5), Iterations);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(9), SaltLength);
        salt.CopyTo(output, 13);
        subkey.CopyTo(output, 13 + SaltLength);
        return Convert.ToBase64String(output);
    }

    public bool Verify(string hash, string password)
    {
        password ??= string.Empty;
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(hash ?? string.Empty);
        }
        catch (FormatException)
        {
            decoded = [];
        }

        if (decoded.Length < 13 || decoded[0] != 0x01)
        {
            // Same work as a real check, so a missing account does not answer faster.
            _ = Rfc2898DeriveBytes.Pbkdf2(password, new byte[SaltLength], Iterations, HashAlgorithmName.SHA512, SubkeyLength);
            return false;
        }

        var prf = BinaryPrimitives.ReadUInt32BigEndian(decoded.AsSpan(1));
        var iterations = (int)BinaryPrimitives.ReadUInt32BigEndian(decoded.AsSpan(5));
        var saltLength = (int)BinaryPrimitives.ReadUInt32BigEndian(decoded.AsSpan(9));
        if (saltLength < 8 || decoded.Length <= 13 + saltLength)
        {
            return false;
        }

        var algorithm = prf switch
        {
            0 => HashAlgorithmName.SHA1,
            1 => HashAlgorithmName.SHA256,
            _ => HashAlgorithmName.SHA512,
        };
        var salt = decoded.AsSpan(13, saltLength);
        var expected = decoded.AsSpan(13 + saltLength);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt.ToArray(), iterations, algorithm, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool NeedsRehash(string hash)
    {
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(hash ?? string.Empty);
        }
        catch (FormatException)
        {
            return true;
        }

        return decoded.Length < 13 || decoded[0] != 0x01
               || BinaryPrimitives.ReadUInt32BigEndian(decoded.AsSpan(1)) != PrfHmacSha512
               || BinaryPrimitives.ReadUInt32BigEndian(decoded.AsSpan(5)) < Iterations;
    }

    public void SimulateVerify(string password) =>
        _ = Rfc2898DeriveBytes.Pbkdf2(password ?? string.Empty, new byte[SaltLength], Iterations, HashAlgorithmName.SHA512, SubkeyLength);

    public string GenerateOneTimePassword() => RandomNumberGenerator.GetString(OneTimeAlphabet, 16);
}
