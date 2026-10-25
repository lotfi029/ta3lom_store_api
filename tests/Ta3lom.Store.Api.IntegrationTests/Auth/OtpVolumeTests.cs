using System.Net;
using System.Net.Http.Json;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Features.CustomerAuth.Commands.RequestOtp;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.Auth;

/// L-10 (D-10): the daily cap and the half-cap alert are checked one request at a time, so parallel requests from
/// different IPs for different mobiles raise the alert once.
public sealed class OtpVolumeTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    [Fact]
    [Rule("D-10")]
    public async Task Parallel_requests_crossing_half_the_daily_cap_alert_staff_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var halfCapMinusOne = (SessionPolicy.OtpDailyGlobalCap / 2) - 1;
        await factory.ExecuteSqlAsync($"""
            INSERT INTO store.otp_challenge (id, mobile, purpose, code_hash, attempts, expires_at, created_at)
            SELECT gen_random_uuid(), '+9665' || lpad(n::text, 8, '7'), 'login', repeat('0', 64), 0, now() + interval '5 minutes', now()
              FROM generate_series(1, {halfCapMinusOne - await factory.ScalarAsync<long>("SELECT count(*) FROM store.otp_challenge")}) AS n
            """);

        var responses = await Task.WhenAll(Enumerable.Range(1, 10).Select(i =>
            factory.Browser($"192.0.2.{i}").PostAsJsonAsync("/v1/auth/otp/request", new { mobile = ScenarioFactory.NewMobile() }, ct)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Accepted);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = 'StaffAlert' AND payload->>'kind' = '{RequestOtpCommandHandler.VolumeAlertKind}'"))
            .ShouldBe(1);
    }
}
