namespace Ta3lom.Store.Application.Interfaces;

/// Staff passwords (09 §5.8): ASP.NET Core Identity v3 format (PBKDF2), constant-time verification.
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string hash, string password);

    /// A random one-time password for new staff and resets (shown once, 11 §9.19).
    string GenerateOneTimePassword();
}

/// Staff TOTP (RUL-62): 30-second step, window ±1; the secret is stored Data-Protection-encrypted.
public interface ITotpService
{
    /// A new secret: the base32 text for the authenticator app and the protected bytes for `staff_user.totp_secret_enc`.
    TotpEnrolment CreateEnrolment(string accountEmail);

    bool Verify(byte[] protectedSecret, string code);
}

public sealed record TotpEnrolment(string OtpauthUri, string SecretBase32, byte[] ProtectedSecret);
