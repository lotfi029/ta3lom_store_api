using System.Net;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;

namespace Ta3lom.Store.Api.IntegrationTests.Http;

/// API headers; OpenAPI and Scalar exist in Development only.
public sealed class SecurityHeadersTests(StoreApiFactory factory) : IClassFixture<StoreApiFactory>
{
    private const string Csp = "default-src 'none'; frame-ancestors 'none'";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/v1/unknown")]
    public async Task Every_response_has_the_security_headers(string path)
    {
        var response = await factory.CreateClient().GetAsync(path, Ct);

        Header(response, "X-Content-Type-Options").ShouldBe("nosniff");
        Header(response, "X-Frame-Options").ShouldBe("DENY");
        Header(response, "Referrer-Policy").ShouldBe("strict-origin-when-cross-origin");
        Header(response, "Content-Security-Policy").ShouldBe(Csp);
        response.Headers.Contains("Server").ShouldBeFalse();
    }

    [Theory]
    [InlineData("/v1/me")]
    [InlineData("/v1/auth/refresh")]
    [InlineData("/v1/admin/products")]
    [InlineData("/v1/checkout")]
    [InlineData("/v1/cart/lines")]
    [InlineData("/v1/orders/T3-26-000001/status")]
    public async Task Cookie_and_bearer_areas_are_never_cached(string path)
    {
        var response = await factory.CreateClient().GetAsync(path, Ct);

        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/v1/catalog/products")]
    [InlineData("/v1/cartoon")]
    public async Task Other_paths_are_not_forced_to_no_store(string path)
    {
        var response = await factory.CreateClient().GetAsync(path, Ct);

        (response.Headers.CacheControl?.NoStore ?? false).ShouldBeFalse();
    }

    [Theory]
    [InlineData("/docs")]
    [InlineData("/openapi/v1.json")]
    public async Task Docs_and_openapi_are_not_mapped_outside_development(string path)
    {
        var response = await factory.CreateClient().GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values)
        : response.Content.Headers.TryGetValues(name, out var contentValues) ? string.Join(",", contentValues)
        : null;
}

public sealed class ProductionHeadersTests(ProductionApiFactory factory) : IClassFixture<ProductionApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/docs")]
    [InlineData("/docs/")]
    [InlineData("/openapi/v1.json")]
    public async Task Docs_and_openapi_are_not_mapped_in_production(string path)
    {
        var response = await factory.CreateClient().GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.Contains("Content-Security-Policy").ShouldBeTrue();
    }

    [Fact]
    public async Task Hsts_is_sent_when_the_known_proxy_forwards_https()
    {
        using var request = HttpsViaProxy(StoreApiFactory.KnownProxyIp);

        var response = await factory.CreateClient().SendAsync(request, Ct);

        response.Headers.GetValues("Strict-Transport-Security").Single().ShouldBe("max-age=31536000; includeSubDomains");
    }

    [Fact]
    public async Task Forwarded_proto_from_an_unknown_peer_is_ignored()
    {
        using var request = HttpsViaProxy(StoreApiFactory.UnknownClientIp);

        var response = await factory.CreateClient().SendAsync(request, Ct);

        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    [Fact]
    public async Task Production_CORS_allows_the_public_site_origins()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");

        var response = await factory.CreateClient().SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);   // start-up validation passed with appsettings.Production.json origins
    }

    private static HttpRequestMessage HttpsViaProxy(string peerIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://store-api.ta3lom.sa/health/live");
        request.Headers.Add(RemoteIpStartupFilter.Header, peerIp);
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "198.51.100.7");
        return request;
    }
}

/// Development maps OpenAPI and Scalar; they get no CSP and no frame header so Scalar can load.
public sealed class DevelopmentDocsTests(DevelopmentApiFactory factory) : IClassFixture<DevelopmentApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/docs/")]
    [InlineData("/openapi/v1.json")]
    public async Task Docs_and_openapi_have_no_CSP_or_frame_header_in_development(string path)
    {
        var response = await factory.CreateClient().GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
        response.Headers.Contains("X-Frame-Options").ShouldBeFalse();
    }

    [Fact]
    public async Task Scalar_page_loads_in_development()
    {
        var html = await factory.CreateClient().GetStringAsync("/docs", Ct);

        html.ShouldContain("scalar");
    }
}
