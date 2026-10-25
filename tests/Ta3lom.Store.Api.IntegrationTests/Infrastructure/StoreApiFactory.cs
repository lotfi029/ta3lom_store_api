using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;

namespace Ta3lom.Store.Api.IntegrationTests.Infrastructure;

/// Hosts the real Program in-memory in the environment "Testing" — not Development, so `/dev`, OpenAPI and the DEV ONLY
/// key fallback are off and the factory supplies throw-away keys. Settings are applied with UseSetting so they win over
/// appsettings*.json before Program reads configuration.
public class StoreApiFactory : WebApplicationFactory<Program>
{
    public const string PublicBaseUrl = "https://store-api.ta3lom.sa";
    public const string WebOrigin = "https://store.ta3lom.sa";
    public const string StagingOrigin = "https://store-staging.ta3lom.sa";

    /// Proxy network trusted for X-Forwarded-* in tests (the Docker network in production).
    public const string KnownProxyIp = "10.0.0.2";

    public const string UnknownClientIp = "203.0.113.10";

    protected virtual string EnvironmentName => "Testing";

    /// The migrated and seeded shared database; a test can point a factory elsewhere (e.g. an unreachable server).
    protected virtual string ConnectionString => SharedStoreDatabase.ConnectionString;

    /// Throw-away keys (never real ones): every environment except Development refuses missing or DEV ONLY keys.
    public static IReadOnlyDictionary<string, string?> TestKeys { get; } = new Dictionary<string, string?>
    {
        ["Store:Jwt:CustomerSigningKeyBase64"] = TestKey(1),
        ["Store:Jwt:StaffSigningKeyBase64"] = TestKey(2),
        ["Store:Otp:PepperBase64"] = TestKey(3),
        ["Store:Tokens:StatusKeyBase64"] = TestKey(4),
        ["Store:Hashing:KeyBase64"] = TestKey(5),
    };

    /// Settings applied on top of appsettings*.json.
    protected virtual IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(TestKeys)
    {
        ["Store:PublicBaseUrl"] = PublicBaseUrl,
        ["Store:ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8",
        ["Serilog:MinimumLevel:Default"] = "Fatal",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("ConnectionStrings:Store", ConnectionString);
        builder.UseSetting("Store:DataProtection:KeysPath", StoreSchema.DataProtectionKeysPath);
        builder.UseSetting("Store:Seed:RunOnStartup", "false");   // the shared database is seeded once by the fixture
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

    public static string TestKey(byte seed) => Convert.ToBase64String(Enumerable.Repeat(seed, 32).ToArray());
}

/// Registers the test-only /v1 probe endpoints. Production code maps no probe route.
public class ProbeApiFactory : StoreApiFactory
{
    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddSingleton<Ta3lom.Store.Api.Endpoints.IEndpoint, ProbeEndpoints>();
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

/// Production environment: appsettings.Production.json, no /docs, HSTS on.
public class ProductionApiFactory : StoreApiFactory
{
    protected override string EnvironmentName => "Production";

    /// Production also requires the site-rebuild, SMS and SMTP settings (throw-away values; the outbox is off, nothing is sent).
    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Store:SiteRebuild:Repository"] = "example/web",
        ["Store:SiteRebuild:Token"] = "test-token-not-real",
        ["Store:Sms:AppSid"] = "test-app-sid-not-real",
        ["Store:Sms:SenderId"] = "ta3lom",
        ["Store:Email:User"] = "store@ta3lom.sa",
        ["Store:Email:Password"] = "test-password-not-real",
        ["Store:Outbox:Enabled"] = "false",
    };
}

/// Production without `Store:SiteRebuild`: must refuse to start.
public sealed class ProductionWithoutRebuildFactory : ProductionApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Settings =>
        base.Settings.Where(s => !s.Key.StartsWith("Store:SiteRebuild", StringComparison.Ordinal)).ToDictionary();
}

/// Development environment: `/dev` and OpenAPI are mapped, DEV ONLY keys are accepted.
public sealed class DevelopmentApiFactory : StoreApiFactory
{
    protected override string EnvironmentName => "Development";

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Store:PublicBaseUrl"] = PublicBaseUrl,
        ["Serilog:MinimumLevel:Default"] = "Fatal",
        ["Store:Files:Root"] = Path.Combine(Path.GetTempPath(), $"ta3lom-store-dev-files-{Guid.NewGuid():N}"),
        ["Store:Outbox:Enabled"] = "false",
    };

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddSingleton<Ta3lom.Store.Api.Endpoints.IEndpoint, ProbeEndpoints>();
}
