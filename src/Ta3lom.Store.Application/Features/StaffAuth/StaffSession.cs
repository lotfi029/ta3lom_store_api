using Ta3lom.Store.Application.DTOs.Staff;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Application.Features.StaffAuth;

/// TOTP verify / refresh / password change output: the JSON body plus the raw refresh token for the `t3s_srt` cookie.
public sealed record StaffSession(StaffSessionResponse Body, string RefreshToken, DateTimeOffset RefreshExpiresAt);

/// Issues staff tokens. The `pwd_change` claim is computed from `must_change_password` on every issue (R-5), so a forced
/// change survives a refresh and disappears as soon as the password is changed.
internal sealed class StaffSessions(IRefreshTokenRepository refreshTokens, IRefreshTokenCodec codec, IAccessTokenIssuer issuer)
{
    /// A new family of 8 hours (D-11).
    public StaffSession Start(StaffUser user, string? userAgent, DateTimeOffset now)
    {
        var (raw, hash) = codec.Create();
        var refresh = RefreshToken.StartFamily(TokenSubjectType.Staff, user.Id, hash, SessionPolicy.StaffFamilyLifetime, SessionPolicy.StaffFamilyLifetime, userAgent, now);
        refreshTokens.Add(refresh);
        return Issue(user, raw, refresh.ExpiresAt);
    }

    /// Rotation inside the family.
    public StaffSession Rotate(StaffUser user, RefreshToken current, string? userAgent, DateTimeOffset now)
    {
        var (raw, hash) = codec.Create();
        var next = current.Rotate(hash, SessionPolicy.StaffFamilyLifetime, userAgent, now);
        refreshTokens.Add(next);
        return Issue(user, raw, next.ExpiresAt);
    }

    public static StaffMeResponse Me(StaffUser user) => new(user.Id, user.Email, user.Name, user.Roles, user.MustChangePassword, user.LastLoginAt);

    private StaffSession Issue(StaffUser user, string rawRefresh, DateTimeOffset refreshExpiresAt)
    {
        var access = issuer.IssueStaff(user.Id, user.Email, user.Roles, user.MustChangePassword);
        return new StaffSession(new StaffSessionResponse(access.Token, access.ExpiresInSeconds, Me(user)), rawRefresh, refreshExpiresAt);
    }
}
