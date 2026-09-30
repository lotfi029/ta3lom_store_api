using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ta3lom.Store.Api.IntegrationTests.Infrastructure;

/// Hosts the real Program in-memory. Environment "Testing" (non-production: /docs is mapped).
/// Settings are overridden with UseSetting so they apply before Program reads configuration.
public class StoreApiFactory : WebApplicationFactory<Program>
{
    public const string PublicBaseUrl = "https://store-api.ta3lom.sa";

    /// Proxy network trusted for X-Forwarded-* in tests (the Docker network in production).
    public const string KnownProxyIp = "10.0.0.2";

    public const string UnknownClientIp = "203.0.113.10";

    protected virtual string EnvironmentName => "Testing";

    /// Settings applied on top of appsettings*.json.
    protected virtual IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Store:PublicBaseUrl"] = PublicBaseUrl,
        ["Store:Cors:Origins:0"] = "https://ta3lom.sa",
        ["Store:Cors:Origins:1"] = "https://www.ta3lom.sa",
        ["Store:ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8",
        ["Serilog:MinimumLevel:Default"] = "Fatal",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, RemoteIpStartupFilter>());
            ConfigureTestServices(services);
        });
    }

    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }
}

/// Registers the test-only /v1 probe endpoints (A1). Production code maps no probe route.
public class ProbeApiFactory : StoreApiFactory
{
    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddSingleton<Ta3lom.Store.Api.Endpoints.IV1EndpointContributor, ProbeContributor>();
}

public sealed class RateLimitedApiFactory : ProbeApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Store:RateLimits:Public:TokenLimit"] = "2",
        ["Store:RateLimits:Public:TokensPerPeriod"] = "1",
        ["Store:RateLimits:Public:ReplenishmentPeriodSeconds"] = "3600",
    };
}

/// Production environment: appsettings.Production.json origins, no /docs, HSTS on.
public sealed class ProductionApiFactory : StoreApiFactory
{
    protected override string EnvironmentName => "Production";

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Store:ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8",
        ["Serilog:MinimumLevel:Default"] = "Fatal",
    };
}

public sealed class StagingApiFactory : ProbeApiFactory
{
    protected override string EnvironmentName => "Staging";

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Serilog:MinimumLevel:Default"] = "Fatal",
    };
}
