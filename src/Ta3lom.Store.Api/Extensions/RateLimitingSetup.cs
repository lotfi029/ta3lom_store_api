using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Api.Options;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.Extensions;

/// In-memory limiter per container (09 §5.15). Limits come from Store:RateLimits:* so tests can shrink them.
/// Partition key: the client IP after ForwardedHeaders (HMAC'd once the IP-hash key exists, S-04).
public static class RateLimitingSetup
{
    public static IServiceCollection AddStoreRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = OnRejectedAsync;
        });

        services.AddOptions<RateLimiterOptions>().Configure<IOptions<StoreOptions>>((o, store) =>
        {
            var p = store.Value.RateLimits.Public;
            o.AddPolicy(RatePolicies.Public, http => RateLimitPartition.GetTokenBucketLimiter(
                ClientKey(http),
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = p.TokenLimit,
                    TokensPerPeriod = p.TokensPerPeriod,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(p.ReplenishmentPeriodSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        });
        return services;
    }

    private static string ClientKey(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static async ValueTask OnRejectedAsync(OnRejectedContext ctx, CancellationToken ct)
    {
        var http = ctx.HttpContext;
        var retryAfter = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra)
            ? ra
            : TimeSpan.FromSeconds(http.RequestServices.GetRequiredService<IOptions<StoreOptions>>().Value.RateLimits.Public.ReplenishmentPeriodSeconds);
        http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        var problems = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = { Status = StatusCodes.Status429TooManyRequests, Extensions = { ["code"] = ErrorCodes.RateLimited } },
        });
    }
}
