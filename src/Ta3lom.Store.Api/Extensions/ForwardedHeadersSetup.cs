using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Api.Options;

namespace Ta3lom.Store.Api.Extensions;

/// TLS ends at Caddy (08 §4). X-Forwarded-For/Proto are trusted only from the configured proxy
/// networks; the framework's loopback defaults are cleared.
public static class ForwardedHeadersSetup
{
    public static IServiceCollection AddStoreForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<StoreOptions>>((o, store) =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
            foreach (var network in store.Value.ForwardedHeaders.KnownNetworks)
            {
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }

            foreach (var proxy in store.Value.ForwardedHeaders.KnownProxies)
            {
                o.KnownProxies.Add(IPAddress.Parse(proxy));
            }
        });
        return services;
    }
}
