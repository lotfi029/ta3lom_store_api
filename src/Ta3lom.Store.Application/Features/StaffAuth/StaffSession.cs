using Ta3lom.Store.Application.DTOs.Staff;

namespace Ta3lom.Store.Application.Features.StaffAuth;

/// TOTP verify / refresh output: the JSON body plus the raw refresh token for the `t3s_srt` cookie.
public sealed record StaffSession(StaffSessionResponse Body, string RefreshToken, DateTimeOffset RefreshExpiresAt);
