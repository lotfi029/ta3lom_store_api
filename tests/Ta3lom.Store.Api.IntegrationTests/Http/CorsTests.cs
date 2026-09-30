using System.Net;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;

namespace Ta3lom.Store.Api.IntegrationTests.Http;

/// CORS on the /v1 group (08 §3.1, 11 §1.2), exercised through the test-only probe route.
public sealed class CorsTests(ProbeApiFactory factory) : IClassFixture<ProbeApiFactory>
{
    private const string Ping = "/v1/__test/ping";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("https://ta3lom.sa")]
    [InlineData("https://www.ta3lom.sa")]
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
    [InlineData("http://ta3lom.sa")]
    [InlineData("https://staging.ta3lom.sa")]
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
        request.Headers.Add("Origin", "https://ta3lom.sa");

        var response = await factory.CreateClient().SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin").Single().ShouldBe("https://ta3lom.sa");
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

public sealed class StagingCorsTests(StagingApiFactory factory) : IClassFixture<StagingApiFactory>
{
    [Fact]
    public async Task Staging_allows_only_the_staging_site()
    {
        var client = factory.CreateClient();

        var staging = await client.SendAsync(CorsTests.Preflight("https://staging.ta3lom.sa"), TestContext.Current.CancellationToken);
        var production = await client.SendAsync(CorsTests.Preflight("https://ta3lom.sa"), TestContext.Current.CancellationToken);

        staging.Headers.GetValues("Access-Control-Allow-Origin").Single().ShouldBe("https://staging.ta3lom.sa");
        production.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }
}
