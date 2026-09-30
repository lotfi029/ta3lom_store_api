using Ta3lom.Store.Application.DTOs.CustomerAuth;

namespace Ta3lom.Store.Application.Features.CustomerAuth;

/// Handler output: the JSON body plus the raw refresh token the endpoint puts in the `t3s_rt` cookie.
public sealed record CustomerSession(SessionResponse Body, string RefreshToken, DateTimeOffset RefreshExpiresAt);

/// Refresh output: new access token + rotated refresh cookie.
public sealed record RotatedSession(AccessTokenResponse Body, string RefreshToken, DateTimeOffset RefreshExpiresAt);

/// No-op until S-13 registers the real cart merge (RUL-17).
internal sealed class NoCartMergeHook : ICartMergeHook
{
    public Task<IReadOnlyList<CartMergeNotice>> MergeGuestCartAsync(Guid? guestCartId, Guid customerId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CartMergeNotice>>([]);
}
