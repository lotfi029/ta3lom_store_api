using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Infrastructure.Services.Security;

namespace Ta3lom.Store.Api.IntegrationTests.Http;

/// Production with one setting replaced (null removes it).
public sealed class GuardedProductionFactory(string key, string? value, string environment = "Production") : ProductionApiFactory
{
    protected override string EnvironmentName => environment;

    protected override IReadOnlyDictionary<string, string?> Settings =>
        base.Settings.Where(s => s.Key != key).Concat(value is null ? [] : [new KeyValuePair<string, string?>(key, value)]).ToDictionary();
}

/// Every environment except Development refuses missing or DEV ONLY keys at start-up.
public sealed class StartupGuardTests
{
    [Theory]
    [InlineData("Store:Jwt:CustomerSigningKeyBase64", DevOnlyKeys.JwtCustomer, "Production")]
    [InlineData("Store:Jwt:StaffSigningKeyBase64", null, "Production")]
    [InlineData("Store:Otp:PepperBase64", DevOnlyKeys.OtpPepper, "Staging")]
    [InlineData("Store:Tokens:StatusKeyBase64", null, "Staging")]
    [InlineData("Store:Hashing:KeyBase64", DevOnlyKeys.Hashing, "Testing")]
    [InlineData("Store:Hashing:KeyBase64", "c2hvcnQ=", "Production")]
    [InlineData("Store:Jwt:CustomerSigningKeyBase64", DevOnlyKeys.JwtCustomer + "\n", "Production")]       // L-1: same bytes
    [InlineData("Store:Tokens:StatusKeyBase64", DevOnlyKeys.Hashing, "Production")]                          // L-1: another DEV ONLY key
    [InlineData("Store:Sms:AppSid", null, "Production")]                                                     // A-8
    [InlineData("Store:Sms:SenderId", "", "Production")]
    [InlineData("Store:Sms:Provider", "sink", "Production")]
    [InlineData("Store:Email:Password", null, "Production")]
    [InlineData("Store:Email:Mode", "sink", "Production")]
    public void Missing_short_or_dev_only_keys_stop_the_host(string key, string? value, string environment)
    {
        using var factory = new GuardedProductionFactory(key, value, environment);

        var failure = Should.Throw<Exception>(() => factory.CreateClient());

        failure.ToString().ShouldContain(key);
    }

    /// R12-4: the same key bytes written differently (here with a trailing newline) are still the same key.
    [Fact]
    public void The_customer_and_staff_keys_must_differ_as_bytes()
    {
        using var factory = new GuardedProductionFactory("Store:Jwt:StaffSigningKeyBase64", StoreApiFactory.TestKey(1) + "\n");

        var failure = Should.Throw<Exception>(() => factory.CreateClient());

        failure.ToString().ShouldContain("customer and staff keys must differ");
    }
}

/// L-9: outside Development the key ring path must be set and writable.
public sealed class KeyRingGuardTests
{
    [Fact]
    public void An_empty_key_ring_path_stops_the_host()
    {
        using var factory = new GuardedProductionFactory("Store:DataProtection:KeysPath", "");

        Should.Throw<Exception>(() => factory.CreateClient()).ToString().ShouldContain("Store:DataProtection:KeysPath");
    }

    [Fact]
    public void An_unwritable_key_ring_path_stops_the_host()
    {
        var file = Path.GetTempFileName();                      // a file cannot hold a directory: not writable as a key ring
        try
        {
            using var factory = new GuardedProductionFactory("Store:DataProtection:KeysPath", Path.Combine(file, "keys"));

            Should.Throw<Exception>(() => factory.CreateClient()).ToString().ShouldContain("is not writable");
        }
        finally
        {
            File.Delete(file);
        }
    }
}

/// A-4: citext and pg_trgm live in `public`; a search path without it breaks case-insensitive e-mail lookups.
public sealed class SearchPathGuardTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void A_search_path_without_public_stops_the_host(string environment)
    {
        using var factory = new GuardedProductionFactory("ConnectionStrings:Store", "Host=localhost;Database=store;Username=u;Password=p;Search Path=store", environment);

        var failure = Should.Throw<Exception>(() => factory.CreateClient());

        failure.ToString().ShouldContain("Search Path");
    }
}

/// Deny by default: every mapped route states its access — a policy or AllowAnonymous.
public sealed class EndpointAccessTests(StoreApiFactory factory) : IClassFixture<StoreApiFactory>
{
    [Fact]
    public void Every_route_declares_a_policy_or_anonymous_access()
    {
        factory.CreateClient().Dispose();

        var undeclared = factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy is not null))
            .Select(e => e.RoutePattern.RawText).ToList();

        undeclared.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_path_is_still_404()
    {
        var response = await factory.CreateClient().GetAsync("/v1/nothing-here", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }
}
