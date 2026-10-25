namespace Ta3lom.Store.Application.Interfaces;

/// Staff passwords (D-12): PBKDF2-SHA512 with 600,000 iterations in the ASP.NET Core Identity v3 format; constant-time check.
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string hash, string password);

    /// The hash was made with weaker parameters than the current ones: rehash after a successful sign-in.
    bool NeedsRehash(string hash);

    /// Does the work of a real verification, so an unknown e-mail takes as long as a wrong password.
    void SimulateVerify(string password);

    /// A random one-time password for new staff and resets (shown once).
    string GenerateOneTimePassword();
}

/// Staff TOTP (D-12): 30-second step, window ±1; the secret is stored Data-Protection-encrypted.
public interface ITotpService
{
    /// A new secret: the base32 text for the authenticator app and the protected bytes for `staff_user.totp_secret_enc`.
    TotpEnrolment CreateEnrolment(string accountEmail);

    /// The matched time step (null when the code is wrong), or `KeyUnavailable` when the stored secret can no longer be
    /// decrypted (the Data Protection key ring was lost). The caller refuses a step that was already used.
    TotpCheck VerifyStep(byte[] protectedSecret, string code);
}

public sealed record TotpCheck(long? Step, bool KeyUnavailable = false);

public sealed record TotpEnrolment(string OtpauthUri, string SecretBase32, byte[] ProtectedSecret);
