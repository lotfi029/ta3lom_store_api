using System.Net;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Http;

/// `public` policy (09 §5.15) with test-sized limits from Store:RateLimits:Public.
public sealed class RateLimitTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Third_request_in_the_window_is_429_with_Retry_After_and_problem()
    {
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        (await client.GetAsync("/v1/__test/ping", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/v1/__test/ping", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var limited = await client.GetAsync("/v1/__test/ping", ct);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        int.Parse(limited.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThan(0);
        var problem = await limited.ReadProblemAsync();
        problem.Str("code").ShouldBe(ErrorCodes.RateLimited);
        problem.Str("type").ShouldBe($"{StoreApiFactory.PublicBaseUrl}/problems/http.rate_limited");
        problem.Str("title").ShouldBe("طلبات كثيرة");
    }

    [Fact]
    public async Task Limits_are_partitioned_per_client_ip()
    {
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/__test/ping");
            request.Headers.Add(RemoteIpStartupFilter.Header, "198.51.100.44");
            (await client.SendAsync(request, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Health_is_not_rate_limited()
    {
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
            request.Headers.Add(RemoteIpStartupFilter.Header, "198.51.100.45");
            (await client.SendAsync(request, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }
}
