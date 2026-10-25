using System.Net;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;

namespace Ta3lom.Store.Api.IntegrationTests.Http;

/// CORS on the /v1 group: the allow-list is the store web site and its staging copy, exercised through the test-only probe route.
public sealed class CorsTests(ProbeApiFactory factory) : IClassFixture<ProbeApiFactory>
{
    private const string Ping = "/v1/__test/ping";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(StoreApiFactory.WebOrigin)]
    [InlineData(StoreApiFactory.StagingOrigin)]
    public async Task Preflight_from_the_site_is_allowed_with_credentials(string origin)
    {
        var response = await factory.CreateClient().SendAsync(Preflight(origin), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Single().ShouldBe(origin);
        response.Headers.GetValues("Access-Control-Allow-Credentials").Single().ShouldBe("true");
        response.Headers.GetValues("Access-Control-Max-Age").Single().ShouldBe("600");
        var allowedHeaders = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")).ToUpperInvariant();
        allowedHeaders.ShouldContain("AUTHORIZATION");
        allowedHeaders.ShouldContain("IDEMPOTENCY-KEY");
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://store.ta3lom.sa")]
    [InlineData("https://ta3lom.sa")]
    [InlineData("https://store.ta3lom.sa.evil.example")]
    [InlineData("http://localhost:4200")]
    public async Task Preflight_from_any_other_origin_is_refused(string origin)
    {
        var response = await factory.CreateClient().SendAsync(Preflight(origin), Ct);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task Actual_request_exposes_the_contract_headers()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Ping);
        request.Headers.Add("Origin", StoreApiFactory.WebOrigin);

        var response = await factory.CreateClient().SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin").Single().ShouldBe(StoreApiFactory.WebOrigin);
        var exposed = string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers")).Split(',').Select(h => h.Trim());
        exposed.ShouldBe(["ETag", "Retry-After", "Location", "Idempotent-Replayed"], ignoreOrder: true);
    }

    internal static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, Ping);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,idempotency-key");
        return request;
    }
}

/// Development adds the local web app to the same allow-list.
public sealed class DevelopmentCorsTests(DevelopmentApiFactory factory) : IClassFixture<DevelopmentApiFactory>
{
    [Fact]
    public async Task Development_also_allows_localhost()
    {
        var response = await factory.CreateClient().SendAsync(CorsTests.Preflight("http://localhost:4200"), TestContext.Current.CancellationToken);

        response.Headers.GetValues("Access-Control-Allow-Origin").Single().ShouldBe("http://localhost:4200");
    }
}
