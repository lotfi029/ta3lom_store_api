using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ta3lom.Store.Api.Endpoints;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;

namespace Ta3lom.Store.Api.IntegrationTests.Health;

/// #134, #135 (11 §10, FR-S83).
public sealed class HealthTests(StoreApiFactory factory) : IClassFixture<StoreApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Live_returns_only_the_status()
    {
        var response = await factory.CreateClient().GetAsync("/health/live", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("""{"status":"Healthy"}""");
    }

    [Fact]
    public async Task Ready_has_the_contract_shape()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        body.EnumerateObject().Select(p => p.Name).ShouldBe(["status", "totalDurationMs", "checks"]);
        body.GetProperty("status").GetString().ShouldBe("Healthy");
        body.GetProperty("totalDurationMs").ValueKind.ShouldBe(JsonValueKind.Number);
        body.GetProperty("checks").ValueKind.ShouldBe(JsonValueKind.Array);
    }

    [Fact]
    public async Task Degraded_is_200_with_check_details_and_data_but_no_exception_text()
    {
        using var client = WithReadyCheck(
            HealthCheckResult.Degraded("slow provider", new InvalidOperationException("boom-internal"),
                new Dictionary<string, object> { ["pending"] = 612 })).CreateClient();

        var response = await client.GetAsync("/health/ready", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = JsonDocument.Parse(text).RootElement;
        body.GetProperty("status").GetString().ShouldBe("Degraded");
        var check = body.GetProperty("checks")[0];
        check.EnumerateObject().Select(p => p.Name).ShouldBe(["name", "status", "durationMs", "data"]);
        check.GetProperty("name").GetString().ShouldBe("probe");
        check.GetProperty("status").GetString().ShouldBe("Degraded");
        check.GetProperty("data").GetProperty("pending").GetInt32().ShouldBe(612);
        text.ShouldNotContain("boom-internal");
        text.ShouldNotContain("slow provider");
    }

    [Fact]
    public async Task Unhealthy_is_503()
    {
        using var client = WithReadyCheck(HealthCheckResult.Unhealthy("down", new InvalidOperationException("boom-internal"))).CreateClient();

        var response = await client.GetAsync("/health/ready", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var text = await response.Content.ReadAsStringAsync(Ct);
        JsonDocument.Parse(text).RootElement.GetProperty("status").GetString().ShouldBe("Unhealthy");
        text.ShouldNotContain("boom-internal");
    }

    [Fact]
    public async Task Live_ignores_ready_checks()
    {
        using var client = WithReadyCheck(HealthCheckResult.Unhealthy("down")).CreateClient();

        var response = await client.GetAsync("/health/live", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_has_no_CORS()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Origin", "https://ta3lom.sa");

        var response = await factory.CreateClient().SendAsync(request, Ct);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithReadyCheck(HealthCheckResult result) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddHealthChecks().AddCheck("probe", () => result, tags: [HealthEndpoints.ReadyTag])));
}
