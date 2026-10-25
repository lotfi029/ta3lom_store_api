using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Features.CustomerAuth.Commands.RequestOtp;
using Ta3lom.Store.Application.Features.Operations;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Auth;

/// Donor sign-in by mobile + SMS code (BR-A09) and donor sessions (D-10, D-11, D-13, D-14).
public sealed class CustomerAuthTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string RefreshCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("t3s_rt=", StringComparison.Ordinal)).Split(';')[0]["t3s_rt=".Length..];

    private async Task<HttpResponseMessage> RequestAsync(HttpClient client, string mobile) =>
        await client.PostAsJsonAsync("/v1/auth/otp/request", new { mobile }, Ct);

    private string LastCode(string mobile) => System.Text.RegularExpressions.Regex.Match(factory.SmsTo(mobile)[0].Body, @"\d{6}").Value;

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/refresh");
        request.Headers.Add("Cookie", "t3s_rt=" + cookie);
        return await client.SendAsync(request, Ct);
    }

    [Fact]
    [Rule("D-14")]
    public async Task The_first_sign_in_creates_the_donor_with_the_PDPL_consent_and_a_refresh_cookie()
    {
        var browser = factory.Browser();
        var mobile = ScenarioFactory.NewMobile();

        var body = await factory.SignInAsync(browser, mobile);

        body.GetProperty("customer").GetProperty("isNew").GetBoolean().ShouldBeTrue();
        body.GetProperty("customer").GetProperty("mobile").GetString().ShouldBe(ScenarioFactory.E164(mobile));
        body.GetProperty("expiresIn").GetInt32().ShouldBe(900);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.customer WHERE mobile = '{ScenarioFactory.E164(mobile)}' AND pdpl_consent_at IS NOT NULL")).ShouldBe(1);

        var again = await factory.SignInAsync(factory.Browser(), mobile);
        again.GetProperty("customer").GetProperty("isNew").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    [Rule("D-10")]
    public async Task The_answer_does_not_reveal_whether_the_mobile_has_an_account()
    {
        var known = ScenarioFactory.NewMobile();
        await factory.SignInAsync(factory.Browser(), known);

        var forKnown = await RequestAsync(factory.Browser(), known);
        var forUnknown = await RequestAsync(factory.Browser(), ScenarioFactory.NewMobile());

        forKnown.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        forUnknown.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await forKnown.Content.ReadAsStringAsync(Ct)).ShouldBe(await forUnknown.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    [Rule("D-10")]
    public async Task Only_the_HMAC_of_the_code_is_stored()
    {
        var mobile = ScenarioFactory.NewMobile();
        await RequestAsync(factory.Browser(), mobile);

        var hash = await factory.ScalarAsync<string>($"SELECT code_hash FROM store.otp_challenge WHERE mobile = '{ScenarioFactory.E164(mobile)}'");

        hash.Length.ShouldBe(64);
        hash.ShouldNotContain(LastCode(mobile));
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.notification WHERE template_key = 'otp' AND recipient_hash <> '{ScenarioFactory.E164(mobile)}'")).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task An_invalid_mobile_is_refused()
    {
        var response = await RequestAsync(factory.Browser(), "12345");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.OtpMobileInvalid);
    }

    [Fact]
    [Rule("D-10")]
    public async Task A_fourth_code_within_15_minutes_is_refused_with_Retry_After()
    {
        var mobile = ScenarioFactory.NewMobile();
        var browser = factory.Browser();
        for (var i = 0; i < 3; i++)
        {
            (await RequestAsync(browser, mobile)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var fourth = await RequestAsync(browser, mobile);

        fourth.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        int.Parse(fourth.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture).ShouldBeInRange(1, 900);
        (await fourth.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.OtpRateLimited);
    }

    [Fact]
    [Rule("D-10")]
    public async Task Parallel_requests_for_one_mobile_are_counted_under_the_lock()
    {
        var mobile = ScenarioFactory.NewMobile();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => RequestAsync(factory.Browser(), mobile)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Accepted).ShouldBe(SessionPolicy.OtpMaxPerMobile);
        responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests).ShouldBe(8 - SessionPolicy.OtpMaxPerMobile);
    }

    [Fact]
    [Rule("D-10")]
    public async Task Ten_codes_per_IP_per_hour()
    {
        var browser = factory.Browser("198.51.100.251");
        for (var i = 0; i < SessionPolicy.OtpMaxPerIp; i++)
        {
            (await RequestAsync(browser, ScenarioFactory.NewMobile())).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        (await RequestAsync(browser, ScenarioFactory.NewMobile())).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    [Rule("D-10")]
    public async Task Five_wrong_codes_burn_the_challenge()
    {
        var mobile = ScenarioFactory.NewMobile();
        var browser = factory.Browser();
        await RequestAsync(browser, mobile);
        var code = LastCode(mobile);
        var wrong = code == "000000" ? "111111" : "000000";

        var first = await browser.PostAsJsonAsync("/v1/auth/otp/verify", new { mobile, code = wrong }, Ct);
        var firstProblem = await first.ReadProblemAsync();
        firstProblem.Str("code").ShouldBe(ErrorCodes.OtpInvalid);
        firstProblem.GetProperty("attemptsLeft").GetInt32().ShouldBe(4);
        for (var i = 0; i < 3; i++)
        {
            await browser.PostAsJsonAsync("/v1/auth/otp/verify", new { mobile, code = wrong }, Ct);
        }

        var fifth = await browser.PostAsJsonAsync("/v1/auth/otp/verify", new { mobile, code = wrong }, Ct);
        (await fifth.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.OtpTooManyAttempts);
        var right = await browser.PostAsJsonAsync("/v1/auth/otp/verify", new { mobile, code }, Ct);
        (await right.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.OtpTooManyAttempts);
    }

    [Fact]
    [Rule("D-10")]
    public async Task A_code_expires_after_five_minutes()
    {
        var mobile = ScenarioFactory.NewMobile();
        var browser = factory.Browser();
        await RequestAsync(browser, mobile);
        var code = LastCode(mobile);
        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        try
        {
            var response = await browser.PostAsJsonAsync("/v1/auth/otp/verify", new { mobile, code }, Ct);

            (await response.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.OtpExpired);
        }
        finally
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(-5));
        }
    }

    [Fact]
    [Rule("D-10")]
    public async Task Half_of_the_daily_cap_alerts_staff_once()
    {
        await factory.ExecuteSqlAsync($"""
            INSERT INTO store.otp_challenge (id, mobile, purpose, code_hash, attempts, expires_at, created_at)
            SELECT gen_random_uuid(), '+966500000000', 'login', repeat('a', 64), 0, now(), now() FROM generate_series(1, {SessionPolicy.OtpDailyGlobalCap / 2})
            """);
        try
        {
            await RequestAsync(factory.Browser(), ScenarioFactory.NewMobile());
            await RequestAsync(factory.Browser(), ScenarioFactory.NewMobile());

            (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = 'StaffAlert' AND payload::text LIKE '%{RequestOtpCommandHandler.VolumeAlertKind}%'")).ShouldBe(1);
        }
        finally
        {
            await factory.ExecuteSqlAsync("DELETE FROM store.otp_challenge WHERE mobile = '+966500000000'");
        }
    }

    [Fact]
    [Rule("D-13")]
    public async Task A_blocked_donor_cannot_sign_in()
    {
        var mobile = ScenarioFactory.NewMobile();
        await factory.SignInAsync(factory.Browser(), mobile);
        await factory.ExecuteSqlAsync($"UPDATE store.customer SET status = 'blocked' WHERE mobile = '{ScenarioFactory.E164(mobile)}'");
        var browser = factory.Browser();
        (await RequestAsync(browser, mobile)).StatusCode.ShouldBe(HttpStatusCode.Accepted);   // the request does not reveal the block

        var verify = await browser.PostAsJsonAsync("/v1/auth/otp/verify", new { mobile, code = LastCode(mobile) }, Ct);

        verify.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await verify.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.AccountBlocked);
    }

    [Fact]
    [Rule("D-13")]
    public async Task A_blocked_donor_loses_the_refresh_family()
    {
        var mobile = ScenarioFactory.NewMobile();
        var browser = factory.Browser();
        await RequestAsync(browser, mobile);
        var verify = await browser.PostAsJsonAsync("/v1/auth/otp/verify", new { mobile, code = LastCode(mobile) }, Ct);
        var cookie = RefreshCookie(verify);
        await factory.ExecuteSqlAsync($"UPDATE store.customer SET status = 'blocked' WHERE mobile = '{ScenarioFactory.E164(mobile)}'");

        var refresh = await RefreshAsync(factory.Browser(), cookie);

        (await refresh.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.AccountBlocked);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.refresh_token t JOIN store.customer c ON c.id = t.subject_id WHERE c.mobile = '{ScenarioFactory.E164(mobile)}' AND t.revoked_at IS NULL")).ShouldBe(0);
    }

    [Fact]
    [Rule("D-11")]
    public async Task Refresh_rotates_and_a_reused_token_revokes_the_family()
    {
        var mobile = ScenarioFactory.NewMobile();
        var browser = factory.Browser();
        await RequestAsync(browser, mobile);
        var first = RefreshCookie(await browser.PostAsJsonAsync("/v1/auth/otp/verify", new { mobile, code = LastCode(mobile) }, Ct));

        var rotated = await RefreshAsync(factory.Browser(), first);
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = RefreshCookie(rotated);
        second.ShouldNotBe(first);
        (await RefreshAsync(factory.Browser(), first)).StatusCode.ShouldBe(HttpStatusCode.Conflict);   // a parallel tab, within the grace window

        factory.Clock.Advance(SessionPolicy.RefreshRaceGrace + TimeSpan.FromSeconds(1));
        var reused = await RefreshAsync(factory.Browser(), first);

        (await reused.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.RefreshReused);
        (await RefreshAsync(factory.Browser(), second)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.audit_log WHERE action = 'token.reuse_detected'")).ShouldBeGreaterThan(0);
    }

    [Fact]
    [Rule("D-11")]
    public async Task The_family_has_an_absolute_lifetime_of_60_days()
    {
        var mobile = ScenarioFactory.NewMobile();
        var browser = factory.Browser();
        await RequestAsync(browser, mobile);
        var cookie = RefreshCookie(await browser.PostAsJsonAsync("/v1/auth/otp/verify", new { mobile, code = LastCode(mobile) }, Ct));
        var start = factory.Clock.UtcNow;
        try
        {
            for (var day = 25; day <= 50; day += 25)
            {
                factory.Clock.Set(start.AddDays(day));
                var rotated = await RefreshAsync(factory.Browser(), cookie);
                rotated.StatusCode.ShouldBe(HttpStatusCode.OK);
                cookie = RefreshCookie(rotated);
            }

            factory.Clock.Set(start.AddDays(60).AddMinutes(1));
            var expired = await RefreshAsync(factory.Browser(), cookie);

            (await expired.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.RefreshExpired);
            (await factory.ScalarAsync<DateTime>($"SELECT max(expires_at) FROM store.refresh_token t JOIN store.customer c ON c.id = t.subject_id WHERE c.mobile = '{ScenarioFactory.E164(mobile)}'"))
                .ShouldBe(start.AddDays(60).UtcDateTime, TimeSpan.FromSeconds(1));
        }
        finally
        {
            factory.Clock.Set(start);
        }
    }

    [Fact]
    public async Task Logout_revokes_the_token_and_logout_all_every_family()
    {
        var mobile = ScenarioFactory.NewMobile();
        var browser = factory.Browser();
        await RequestAsync(browser, mobile);
        var verify = await browser.PostAsJsonAsync("/v1/auth/otp/verify", new { mobile, code = LastCode(mobile) }, Ct);
        var cookie = RefreshCookie(verify);
        var token = JsonDocument.Parse(await verify.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("accessToken").GetString();
        await factory.SignInAsync(factory.Browser(), mobile);                                       // a second device

        using (var logout = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/logout"))
        {
            logout.Headers.Add("Cookie", "t3s_rt=" + cookie);
            (await factory.Browser().SendAsync(logout, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await RefreshAsync(factory.Browser(), cookie)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var all = factory.Browser();
        all.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        (await all.PostAsync("/v1/auth/logout-all", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.refresh_token t JOIN store.customer c ON c.id = t.subject_id WHERE c.mobile = '{ScenarioFactory.E164(mobile)}' AND t.revoked_at IS NULL")).ShouldBe(0);
        (await factory.Browser().PostAsync("/v1/auth/logout-all", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Rule("D-14")]
    public async Task OTP_challenges_are_deleted_after_24_hours()
    {
        var mobile = ScenarioFactory.NewMobile();
        await RequestAsync(factory.Browser(), mobile);
        await factory.ExecuteSqlAsync($"UPDATE store.otp_challenge SET created_at = now() - interval '25 hours' WHERE mobile = '{ScenarioFactory.E164(mobile)}'");

        var deleted = await factory.RunAsync<PruneOtpCommand, int>(new PruneOtpCommand());

        deleted.Value.ShouldBeGreaterThanOrEqualTo(1);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.otp_challenge WHERE mobile = '{ScenarioFactory.E164(mobile)}'")).ShouldBe(0);
    }
}
