using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Api.Options;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.Extensions;

/// In-memory limiter per container. Limits come from Store:RateLimits:* so tests can shrink them.
/// Partition key: the client IP after ForwardedHeaders (HMAC'd with the hashing key).
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

            var r = store.Value.RateLimits;
            AddFixed(o, RatePolicies.OtpRequest, r.OtpRequest, ClientKey);
            AddFixed(o, RatePolicies.OtpVerify, r.OtpVerify, ClientKey);
            AddSliding(o, RatePolicies.AuthRefresh, r.AuthRefresh, ClientKey);
            AddFixed(o, RatePolicies.StaffLogin, r.StaffLogin, ClientKey);
            AddSliding(o, RatePolicies.Cart, r.Cart, ClientKey);
            AddFixed(o, RatePolicies.Checkout, r.Checkout, SubjectOrClientKey);
            AddSliding(o, RatePolicies.Me, r.Me, SubjectOrClientKey);
            AddFixed(o, RatePolicies.Download, r.Download, SubjectOrClientKey);
            AddSliding(o, RatePolicies.Admin, r.Admin, SubjectOrClientKey);
            AddFixed(o, RatePolicies.TemplateTest, r.TemplateTest, SubjectOrClientKey);
        });
        return services;
    }

    private static void AddFixed(RateLimiterOptions o, string name, WindowSettings s, Func<HttpContext, string> key) =>
        o.AddPolicy(name, http => RateLimitPartition.GetFixedWindowLimiter(key(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = s.PermitLimit,
            Window = TimeSpan.FromSeconds(s.WindowSeconds),
            QueueLimit = 0,
        }));

    private static void AddSliding(RateLimiterOptions o, string name, WindowSettings s, Func<HttpContext, string> key) =>
        o.AddPolicy(name, http => RateLimitPartition.GetSlidingWindowLimiter(key(http), _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = s.PermitLimit,
            Window = TimeSpan.FromSeconds(s.WindowSeconds),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
        }));

    /// The HMAC'd client IP: the raw IP never becomes a partition key held in memory.
    private static string ClientKey(HttpContext http) =>
        http.Connection.RemoteIpAddress is { } ip ? http.RequestServices.GetRequiredService<IPseudonymizer>().HashIp(ip.ToString()) : "unknown";

    private static string SubjectOrClientKey(HttpContext http) => http.User.FindFirst("sub")?.Value ?? ClientKey(http);

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
