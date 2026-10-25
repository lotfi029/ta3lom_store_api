namespace Ta3lom.Store.Application.Interfaces;

/// Signed access tokens (D-11): customer audience `store-customer` (15 min), staff `store-staff` (10 min).
public interface IAccessTokenIssuer
{
    AccessToken IssueCustomer(Guid customerId);

    /// <paramref name="passwordChangeRequired"/> adds the `pwd_change=true` claim: only the password change and logout are
    /// allowed with such a token.
    AccessToken IssueStaff(Guid staffId, string email, IReadOnlyCollection<string> roles, bool passwordChangeRequired);

    /// Short-lived token between password and TOTP (audience `store-staff-mfa`, 5 min). Single use: its `jti` is spent by
    /// the TOTP verification.
    AccessToken IssueStaffMfa(Guid staffId);
}

public sealed record AccessToken(string Token, int ExpiresInSeconds, string Jti, DateTimeOffset ExpiresAt);

/// Opaque refresh tokens: 32 random bytes (base64url) in the cookie, SHA-256 hex in the database.
public interface IRefreshTokenCodec
{
    (string Raw, string Hash) Create();

    string Hash(string raw);
}

/// Single-use token ids (the MFA step token). Kept until the token expires.
public interface IUsedTokenStore
{
    /// False when <paramref name="jti"/> was already used.
    bool TryUse(string jti, DateTimeOffset expiresAt);
}
