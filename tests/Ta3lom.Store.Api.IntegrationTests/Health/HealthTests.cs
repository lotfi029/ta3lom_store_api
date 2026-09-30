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
        var postgres = body.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "postgres");
        postgres.GetProperty("status").GetString().ShouldBe("Healthy");
    }

    [Fact]
    public async Task Ready_is_503_when_the_database_is_unreachable()
    {
        await using var unreachable = new UnreachableDatabaseFactory();

        var response = await unreachable.CreateClient().GetAsync("/health/ready", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var postgres = JsonDocument.Parse(text).RootElement.GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "postgres");
        postgres.GetProperty("status").GetString().ShouldBe("Unhealthy");
        text.ShouldNotContain("refused", Case.Insensitive);
        text.ShouldNotContain("PostgreSQL unreachable");
    }

    [Fact]
    public async Task Live_does_not_depend_on_the_database()
    {
        await using var unreachable = new UnreachableDatabaseFactory();

        (await unreachable.CreateClient().GetAsync("/health/live", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private sealed class UnreachableDatabaseFactory : StoreApiFactory
    {
        protected override string ConnectionString => "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2";
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
        var check = body.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "probe");
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
