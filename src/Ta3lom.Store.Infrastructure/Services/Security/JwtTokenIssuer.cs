using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Infrastructure.Services.Security;

/// HMAC-SHA256 JWTs with a `kid` header (D-11): customer (`store-customer`, 15 min), staff (`store-staff`, 10 min, `role`
/// claims, `pwd_change=true` while a forced password change is pending) and the staff MFA step (`store-staff-mfa`, 5 min).
internal sealed class JwtTokenIssuer(IOptions<JwtOptions> options, IClock clock) : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public static SymmetricSecurityKey Key(string base64, string keyId) => new(Convert.FromBase64String(base64)) { KeyId = keyId };

    public AccessToken IssueCustomer(Guid customerId) =>
        Issue(options.Value.CustomerAudience, options.Value.CustomerSigningKeyBase64, SessionPolicy.CustomerAccessLifetime,
            [new(JwtRegisteredClaimNames.Sub, customerId.ToString())]);

    public AccessToken IssueStaff(Guid staffId, string email, IReadOnlyCollection<string> roles, bool passwordChangeRequired)
    {
        ArgumentNullException.ThrowIfNull(roles);
        List<Claim> claims = [new(JwtRegisteredClaimNames.Sub, staffId.ToString()), new(JwtRegisteredClaimNames.Email, email)];
        claims.AddRange(roles.Select(r => new Claim(StoreClaims.Role, r)));
        if (passwordChangeRequired)
        {
            claims.Add(new Claim(StoreClaims.PasswordChangeRequired, "true"));
        }

        return Issue(options.Value.StaffAudience, options.Value.StaffSigningKeyBase64, SessionPolicy.StaffAccessLifetime, claims);
    }

    public AccessToken IssueStaffMfa(Guid staffId) =>
        Issue(options.Value.StaffMfaAudience, options.Value.StaffSigningKeyBase64, SessionPolicy.StaffMfaLifetime,
            [new(JwtRegisteredClaimNames.Sub, staffId.ToString())]);

    private AccessToken Issue(string audience, string keyBase64, TimeSpan lifetime, IEnumerable<Claim> claims)
    {
        var o = options.Value;
        var now = clock.UtcNow.UtcDateTime;
        var jti = Guid.CreateVersion7().ToString("N");
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + lifetime,
            Subject = new ClaimsIdentity(claims.Append(new Claim(JwtRegisteredClaimNames.Jti, jti))),
            SigningCredentials = new SigningCredentials(Key(keyBase64, o.KeyId), SecurityAlgorithms.HmacSha256),
        };
        return new AccessToken(_handler.CreateToken(descriptor), (int)lifetime.TotalSeconds, jti, clock.UtcNow + lifetime);
    }
}
