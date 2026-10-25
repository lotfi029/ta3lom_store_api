using Microsoft.Extensions.Caching.Memory;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Security;

/// Spent MFA token ids in the process memory until the token expires (R-6). One API instance is assumed; several instances
/// would need a shared store (a table or a cache server).
internal sealed class MemoryUsedTokenStore(IMemoryCache cache, IClock clock) : IUsedTokenStore
{
    private readonly Lock _gate = new();

    public bool TryUse(string jti, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jti);
        var key = "used-jti:" + jti;
        lock (_gate)
        {
            if (cache.TryGetValue(key, out _))
            {
                return false;
            }

            var lifetime = expiresAt - clock.UtcNow;
            cache.Set(key, true, lifetime > TimeSpan.Zero ? lifetime + TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(1));
            return true;
        }
    }
}
