using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Api.Options;

namespace Ta3lom.Store.Api.Extensions;

/// Browser access from ta3lom.sa. Applied to the /v1 group only:
/// never to /health/* or to payment webhooks. `web`: the store site (Store:Cors:Origins). `badge` (review 04 S-15): the
/// store site + the main site (Store:Cors:BadgeOrigins), GET only and no request headers — the badge stays a simple
/// request — on `GET /v1/cart/summary` alone.
public static class CorsSetup
{
    public const string WebPolicy = "web";
    public const string BadgePolicy = "badge";

    public static IServiceCollection AddStoreCors(this IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<StoreOptions>>((o, store) =>
            o.AddPolicy(WebPolicy, p => p
                .WithOrigins([.. store.Value.Cors.Origins])
                .AllowCredentials()
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
                .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "Accept-Language")
                .WithExposedHeaders("ETag", "Retry-After", "Location", "Idempotent-Replayed", "Content-Disposition")
                .SetPreflightMaxAge(TimeSpan.FromSeconds(600))));
        services.AddOptions<CorsOptions>().Configure<IOptions<StoreOptions>>((o, store) =>
            o.AddPolicy(BadgePolicy, p => p
                .WithOrigins([.. store.Value.Cors.Origins, .. store.Value.Cors.BadgeOrigins])
                .AllowCredentials()
                .WithMethods("GET")
                .SetPreflightMaxAge(TimeSpan.FromSeconds(600))));
        return services;
    }
}
