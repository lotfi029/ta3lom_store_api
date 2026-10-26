using System.Net;
using Microsoft.Extensions.Configuration;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;

namespace Ta3lom.Store.Api.IntegrationTests.Http;

/// API-4: the CORS lists of the real configuration files, per environment. JSON arrays merge by index, so a second entry
/// in the base list would leak into every environment that sets a shorter list; the exact lists are pinned here.
public sealed class ConfigurationOriginsTests
{
    private static string[] Origins(string environment) =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)                                   // the Api's appsettings*.json are copied here
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .Build()
            .GetSection("Store:Cors:Origins").Get<string[]>() ?? [];

    [Fact]
    public void Each_environment_has_exactly_its_own_origins()
    {
        Origins("Production").ShouldBe(["https://store.ta3lom.sa"]);
        Origins("Staging").ShouldBe(["https://store-staging.ta3lom.sa"]);
        Origins("Development").ShouldBe(["https://store.ta3lom.sa", "https://store-staging.ta3lom.sa", "http://localhost:4200"]);
    }
}

/// API-4: a Production host (real appsettings for CORS) refuses the staging site on preflight and on same-origin-checked
/// POSTs, and accepts the store site.
public sealed class ProductionCorsTests(ProductionApiFactory factory) : IClassFixture<ProductionApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpResponseMessage> PreflightAsync(string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/auth/refresh");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        return await factory.CreateClient().SendAsync(request, Ct);
    }

    private async Task<HttpResponseMessage> PostAsync(string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/refresh");
        request.Headers.Add("Origin", origin);
        return await factory.CreateClient().SendAsync(request, Ct);
    }

    [Fact]
    public async Task The_staging_site_is_refused_in_Production()
    {
        (await PreflightAsync(StoreApiFactory.StagingOrigin)).Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();

        var post = await PostAsync(StoreApiFactory.StagingOrigin);

        post.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await post.ReadProblemAsync()).Str("code").ShouldBe("http.origin_forbidden");
    }

    [Fact]
    public async Task The_store_site_is_accepted_in_Production()
    {
        (await PreflightAsync(StoreApiFactory.WebOrigin)).Headers.GetValues("Access-Control-Allow-Origin").Single().ShouldBe(StoreApiFactory.WebOrigin);

        var post = await PostAsync(StoreApiFactory.WebOrigin);

        post.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);                                 // past the origin check: no refresh cookie
    }
}
