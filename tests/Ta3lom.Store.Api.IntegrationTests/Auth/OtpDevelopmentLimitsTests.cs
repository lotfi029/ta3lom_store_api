using System.Net;
using System.Net.Http.Json;
using Ta3lom.Store.Api.IntegrationTests.Http;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;

namespace Ta3lom.Store.Api.IntegrationTests.Auth;

/// Development with the HTTP rate limiter out of the way, so only the D-10 caps of the handler are exercised.
public class DevelopmentOtpFactory : DevelopmentApiFactory
{
    protected virtual IReadOnlyDictionary<string, string?> OtpSettings => new Dictionary<string, string?>();

    protected override IReadOnlyDictionary<string, string?> Settings =>
        new Dictionary<string, string?>(base.Settings.Concat(OtpSettings))
        {
            ["Store:RateLimits:OtpRequest:PermitLimit"] = "100000",
        };
}

/// Development with `Store:Otp:Development:MaxPerIp = 1000` (local end-to-end runs).
public sealed class RaisedOtpCapsFactory : DevelopmentOtpFactory
{
    protected override IReadOnlyDictionary<string, string?> OtpSettings => new Dictionary<string, string?>
    {
        ["Store:Otp:Development:MaxPerIp"] = "1000",
        ["Store:Otp:Development:MaxPerMobile"] = "50",
    };
}

/// API-8: the OTP caps of D-10 can be raised in Development only; every other environment uses the fixed caps and refuses
/// the keys at start-up. Each request uses its own mobile, so only the per-IP cap is exercised.
public sealed class OtpDevelopmentLimitsTests
{
    private static async Task<List<HttpStatusCode>> RequestAsync(StoreApiFactory factory, string ip, int count)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(RemoteIpStartupFilter.Header, ip);
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < count; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/v1/auth/otp/request", new { mobile = ScenarioFactory.NewMobile() }, TestContext.Current.CancellationToken)).StatusCode);
        }

        return statuses;
    }

    [Fact]
    [Rule("D-10")]
    public async Task Development_can_raise_the_per_IP_cap()
    {
        using var factory = new RaisedOtpCapsFactory();

        var statuses = await RequestAsync(factory, $"198.18.{Random.Shared.Next(0, 255)}.{Random.Shared.Next(1, 254)}", 20);

        statuses.ShouldAllBe(s => s == HttpStatusCode.Accepted);
    }

    [Fact]
    [Rule("D-10")]
    public async Task Development_without_the_keys_keeps_the_fixed_cap()
    {
        using var factory = new DevelopmentOtpFactory();

        var statuses = await RequestAsync(factory, $"198.19.{Random.Shared.Next(0, 255)}.{Random.Shared.Next(1, 254)}", 11);

        statuses.Take(10).ShouldAllBe(s => s == HttpStatusCode.Accepted);
        statuses[10].ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData("Production", "Store:Otp:Development:MaxPerIp")]
    [InlineData("Staging", "Store:Otp:Development:MaxPerMobile")]
    [InlineData("Testing", "Store:Otp:Development:MaxPerIp")]
    public void The_keys_stop_the_host_outside_Development(string environment, string key)
    {
        using var factory = new GuardedProductionFactory(key, "1000", environment);

        var failure = Should.Throw<Exception>(() => factory.CreateClient());

        failure.ToString().ShouldContain(key);
    }
}
