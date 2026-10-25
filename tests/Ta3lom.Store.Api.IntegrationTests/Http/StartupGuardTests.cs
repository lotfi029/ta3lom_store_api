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
    public void Missing_short_or_dev_only_keys_stop_the_host(string key, string? value, string environment)
    {
        using var factory = new GuardedProductionFactory(key, value, environment);

        var failure = Should.Throw<Exception>(() => factory.CreateClient());

        failure.ToString().ShouldContain(key);
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
