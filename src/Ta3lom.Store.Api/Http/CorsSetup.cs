using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Api.Options;

namespace Ta3lom.Store.Api.Http;

/// Browser access from ta3lom.sa (08 §3.1, 11 §1.2). Applied to the /v1 group only:
/// never to /health/* or (later) to payment webhooks.
public static class CorsSetup
{
    public const string WebPolicy = "web";

    public static IServiceCollection AddStoreCors(this IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<StoreOptions>>((o, store) =>
            o.AddPolicy(WebPolicy, p => p
                .WithOrigins([.. store.Value.Cors.Origins])
                .AllowCredentials()
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
                .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "Accept-Language")
                .WithExposedHeaders("ETag", "Retry-After", "Location", "Idempotent-Replayed")
                .SetPreflightMaxAge(TimeSpan.FromSeconds(600))));
        return services;
    }
}
