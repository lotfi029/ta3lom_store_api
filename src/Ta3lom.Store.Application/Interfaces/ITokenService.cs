namespace Ta3lom.Store.Application.Interfaces;

/// Signed access tokens (09 §5.8): customer audience `store-customer` (15 min), staff `store-staff` (10 min).
public interface IAccessTokenIssuer
{
    AccessToken IssueCustomer(Guid customerId);

    AccessToken IssueStaff(Guid staffId, string email, IReadOnlyCollection<string> roles);

    /// Short-lived token between password and TOTP (audience `store-staff-mfa`, 5 min).
    AccessToken IssueStaffMfa(Guid staffId);
}

public sealed record AccessToken(string Token, int ExpiresInSeconds);

/// Opaque refresh tokens: 32 random bytes (base64url) in the cookie, SHA-256 hex in the database.
public interface IRefreshTokenCodec
{
    (string Raw, string Hash) Create();

    string Hash(string raw);
}
