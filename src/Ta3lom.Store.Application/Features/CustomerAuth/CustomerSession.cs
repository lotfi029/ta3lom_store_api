using Ta3lom.Store.Application.DTOs.CustomerAuth;

namespace Ta3lom.Store.Application.Features.CustomerAuth;

/// Handler output: the JSON body plus the raw refresh token the endpoint puts in the `t3s_rt` cookie.
public sealed record CustomerSession(SessionResponse Body, string RefreshToken, DateTimeOffset RefreshExpiresAt);

/// Refresh output: new access token + rotated refresh cookie.
public sealed record RotatedSession(AccessTokenResponse Body, string RefreshToken, DateTimeOffset RefreshExpiresAt);
