namespace Ta3lom.Store.Application.Interfaces;

/// HMAC-SHA256 of the OTP with a server pepper; constant-time comparison.
public interface IOtpHasher
{
    string Hash(Guid challengeId, string mobileE164, string code);

    bool Matches(Guid challengeId, string mobileE164, string codeHash, string code);
}
