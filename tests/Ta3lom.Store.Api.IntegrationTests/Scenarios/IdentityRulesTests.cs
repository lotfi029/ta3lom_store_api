using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.Scenarios;

/// T-5 — identity, account data, staff and notification rules (plan 14 S-04, S-05, S-17, S-31: RUL-60 … RUL-63).
public sealed class IdentityRulesTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Ip() => $"192.0.2.{Random.Shared.Next(1, 250)}";

    private async Task RequestAsync(HttpClient c, string mobile, HttpStatusCode expected = HttpStatusCode.Accepted) =>
        (await c.PostAsJsonAsync("/v1/auth/otp/request", new { mobile }, Ct)).StatusCode.ShouldBe(expected);

    private string LastCode(string mobile) => Regex.Match(factory.SmsTo(mobile)[0].Body, @"\d{6}").Value;

    private static Task<(HttpStatusCode Status, JsonElement Body)> VerifyAsync(HttpClient c, string mobile, string code) =>
        c.SendJsonAsync(HttpMethod.Post, "/v1/auth/otp/verify", new { mobile, code });

    [Fact]
    [Rule("RUL-60")]
    public async Task A_code_allows_five_wrong_attempts_and_expires_after_five_minutes()
    {
        var c = factory.Browser(Ip());
        var mobile = ScenarioFactory.NewMobile();
        await RequestAsync(c, mobile);
        var code = LastCode(mobile);
        var wrong = code == "000000" ? "111111" : "000000";

        var (status, first) = await VerifyAsync(c, mobile, wrong);
        status.ShouldBe(HttpStatusCode.BadRequest);
        first.Code().ShouldBe("otp.invalid");
        first.GetProperty("attemptsLeft").GetInt32().ShouldBe(4);
        for (var i = 0; i < 4; i++)
        {
            await VerifyAsync(c, mobile, wrong);
        }

        (await VerifyAsync(c, mobile, code)).Body.Code().ShouldBe("otp.too_many_attempts");               // even the right code

        var other = ScenarioFactory.NewMobile();
        await RequestAsync(c, other);
        var otherCode = LastCode(other);
        var start = factory.Clock.UtcNow;
        try
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(6));
            (await VerifyAsync(c, other, otherCode)).Body.Code().ShouldBe("otp.expired");
        }
        finally
        {
            factory.Clock.Set(start);
        }

        (await factory.ScalarAsync<string>($"SELECT code_hash FROM store.otp_challenge WHERE mobile = '{ScenarioFactory.E164(other)}'")).ShouldNotContain(otherCode);
    }

    [Fact]
    [Rule("RUL-60")]
    public async Task Three_codes_per_mobile_per_15_minutes_and_ten_per_address_per_hour()
    {
        var c = factory.Browser(Ip());
        var mobile = ScenarioFactory.NewMobile();
        for (var i = 0; i < 3; i++)
        {
            await RequestAsync(c, mobile);
        }

        var (_, limited) = await c.SendJsonAsync(HttpMethod.Post, "/v1/auth/otp/request", new { mobile });
        limited.Code().ShouldBe("otp.rate_limited");
        limited.GetProperty("retryAfterSeconds").GetInt32().ShouldBeGreaterThan(0);

        var address = factory.Browser($"203.0.113.{Random.Shared.Next(1, 250)}");
        for (var i = 0; i < 10; i++)
        {
            await RequestAsync(address, ScenarioFactory.NewMobile());
        }

        (await address.SendJsonAsync(HttpMethod.Post, "/v1/auth/otp/request", new { mobile = ScenarioFactory.NewMobile() })).Body.Code().ShouldBe("otp.rate_limited");
        (await c.SendJsonAsync(HttpMethod.Post, "/v1/auth/otp/request", new { mobile = "0412345678" })).Body.Code().ShouldBe("otp.mobile_invalid");
    }

    [Fact]
    [Rule("RUL-60")]
    public async Task Sign_in_returns_a_short_access_token_and_a_rotating_HttpOnly_refresh_cookie()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        client.DefaultRequestHeaders.Add(RemoteIpStartupFilter.Header, Ip());
        var mobile = ScenarioFactory.NewMobile();
        await RequestAsync(client, mobile);
        var verified = await client.PostAsJsonAsync("/v1/auth/otp/verify", new { mobile, code = LastCode(mobile) }, Ct);
        verified.StatusCode.ShouldBe(HttpStatusCode.OK);
        var session = await verified.Content.ReadFromJsonAsync<JsonElement>(Ct);
        session.GetProperty("expiresIn").GetInt32().ShouldBe(900);
        var cookie = verified.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("t3s_rt=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("path=/v1/auth", Case.Insensitive);
        var original = cookie.Split(';')[0];

        async Task<HttpResponseMessage> RefreshAsync(string c)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/refresh");
            request.Headers.Add("Cookie", c);
            return await client.SendAsync(request, Ct);
        }

        var rotated = await RefreshAsync(original);
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var next = rotated.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("t3s_rt=", StringComparison.Ordinal)).Split(';')[0];
        next.ShouldNotBe(original);

        var start = factory.Clock.UtcNow;
        try
        {
            factory.Clock.Advance(TimeSpan.FromSeconds(30));                                            // past the two-tab grace
            var reused = await RefreshAsync(original);
            reused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await reused.Content.ReadFromJsonAsync<JsonElement>(Ct)).Code().ShouldBe("auth.refresh_reused");
            (await RefreshAsync(next)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);               // the whole family is revoked
        }
        finally
        {
            factory.Clock.Set(start);
        }
    }

    [Fact]
    [Rule("RUL-61")]
    public async Task A_blocked_customer_cannot_sign_in_or_check_out()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        var mobile = ScenarioFactory.NewMobile();
        var customer = await factory.CustomerAsync(mobile);
        await customer.PostAsync("/v1/cart/lines", Shop.Donate(await factory.ProductIdAsync("school-bag")));

        (await admin.PostAsync($"/v1/admin/customers/{customer.Id}/block", new { reason = "احتيال" })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var browser = factory.Browser(Ip());
        await RequestAsync(browser, mobile);
        var (status, problem) = await VerifyAsync(browser, mobile, LastCode(mobile));
        status.ShouldBe(HttpStatusCode.Forbidden);
        problem.Code().ShouldBe("auth.account_blocked");
        var (_, quote) = await customer.PostAsync("/v1/checkout/quote");
        (await customer.PostAsync("/v1/checkout", new
        {
            contact = new { fullName = "محظور", email = "b@example.com" },
            isAnonymous = false,
            saveAddress = false,
            paymentMethod = "mada",
            acceptTerms = true,
            expectedGrandTotal = quote.TryGetProperty("grandTotal", out var t) ? t.GetString() : "180.00",
            locale = "ar",
        }, new Dictionary<string, string> { ["Idempotency-Key"] = Guid.NewGuid().ToString() })).Response.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    [Rule("RUL-61")]
    public async Task Export_carries_the_personal_data_and_delete_anonymises_while_keeping_the_orders()
    {
        var mobile = ScenarioFactory.NewMobile();
        var browser = factory.Browser(Ip());
        await factory.SignInAsync(browser, mobile);
        var customer = new ActorClient(browser, Guid.Empty, mobile, null);
        (await customer.SendAsync(HttpMethod.Patch, "/v1/me", new { fullName = "ريم خالد", email = "reem@example.com" })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var order = await factory.PaidOrderAsync(customer, Shop.Donate(await factory.ProductIdAsync("school-bag")));

        var (exported, export) = await customer.GetAsync("/v1/me/export");
        exported.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = export.ToString();
        text.ShouldContain("ريم خالد");
        text.ShouldContain("reem@example.com");
        text.ShouldContain(order.Reference);

        (await customer.SendAsync(HttpMethod.Delete, "/v1/me", new { confirm = true })).Response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.ScalarAsync<string>($"SELECT contact_name FROM store.orders WHERE id = '{order.OrderId}'")).ShouldBe("محذوف / deleted");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.donation_receipt WHERE order_id = '{order.OrderId}'")).ShouldBe(1);   // records kept

        var again = factory.Browser(Ip());
        var session = await factory.SignInAsync(again, mobile);
        session.GetProperty("customer").GetProperty("isNew").GetBoolean().ShouldBeTrue();
        (await new ActorClient(again, Guid.Empty, mobile, null).GetAsync("/v1/me/orders")).Body.GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Fact]
    [Rule("RUL-62")]
    public async Task Staff_and_customer_tokens_are_not_interchangeable_and_a_staff_login_needs_TOTP()
    {
        var customer = await factory.CustomerAsync();
        var staff = await factory.StaffAsync(Roles.Viewer);

        (await customer.GetAsync("/v1/admin/me")).Response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await staff.GetAsync("/v1/me")).Response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var admin = await factory.StaffAsync(Roles.Admin);
        var (created, body) = await admin.PostAsync("/v1/admin/staff", new { email = $"t5-{Guid.NewGuid():N}@test.ta3lom.sa", name = "T5", roles = new[] { "viewer" } });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var email = body.GetProperty("staff").GetProperty("email").GetString();
        var login = factory.Browser(Ip());
        var (ok, mfa) = await login.SendJsonAsync(HttpMethod.Post, "/v1/admin/auth/login", new { email, password = body.GetProperty("oneTimePassword").GetString() });
        ok.ShouldBe(HttpStatusCode.OK);
        var mfaClient = factory.Anonymous();
        mfaClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mfa.GetProperty("mfaToken").GetString());
        (await mfaClient.GetAsync("/v1/admin/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);    // password alone is not a session
    }

    [Fact]
    [Rule("RUL-62")]
    public async Task Five_wrong_passwords_lock_the_staff_account()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        var (_, body) = await admin.PostAsync("/v1/admin/staff", new { email = $"lock-{Guid.NewGuid():N}@test.ta3lom.sa", name = "Lock", roles = new[] { "viewer" } });
        var email = body.GetProperty("staff").GetProperty("email").GetString();
        var login = factory.Browser(Ip());
        for (var i = 0; i < 5; i++)
        {
            (await login.SendJsonAsync(HttpMethod.Post, "/v1/admin/auth/login", new { email, password = "wrong-password-" + i })).Body.Code()
                .ShouldBeOneOf("auth.invalid_credentials", "auth.account_locked");
        }

        var (locked, problem) = await login.SendJsonAsync(HttpMethod.Post, "/v1/admin/auth/login", new { email, password = body.GetProperty("oneTimePassword").GetString() });
        locked.ShouldBe((HttpStatusCode)423);
        problem.Code().ShouldBe("auth.account_locked");
    }

    [Fact]
    [Rule("RUL-62")]
    public async Task Revealing_beneficiary_PII_needs_the_allocation_officer_role_even_for_an_admin_and_is_audited()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        var officer = await factory.StaffAsync(Roles.AllocationOfficer);
        var mobile = ScenarioFactory.NewMobile();
        var (_, b) = await admin.PostAsync("/v1/admin/beneficiaries", new
        {
            kind = "teacher",
            beneficiaryGroup = "rural_private_school_teachers",
            region = "تبوك",
            fullName = "منى سالم",
            mobile,
            organisationName = "مدرسة",
        });
        var id = b.GetProperty("id").GetGuid();
        b.ToString().ShouldNotContain("منى سالم");

        (await admin.PostAsync($"/v1/admin/beneficiaries/{id}/reveal", new { reason = "تحقق" })).Response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var (revealed, pii) = await officer.PostAsync($"/v1/admin/beneficiaries/{id}/reveal", new { reason = "تحقق" });

        revealed.StatusCode.ShouldBe(HttpStatusCode.OK);
        pii.GetProperty("fullName").GetString().ShouldBe("منى سالم");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = 'beneficiary.reveal' AND actor = '{officer.Email}'")).ShouldBe(1);
    }

    [Fact]
    [Rule("RUL-63")]
    public async Task Notifications_go_through_the_outbox_except_the_OTP_SMS()
    {
        var outboxBefore = await factory.ScalarAsync<long>("SELECT count(*) FROM store.outbox_message");
        var mobile = ScenarioFactory.NewMobile();
        await RequestAsync(factory.Browser(Ip()), mobile);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.outbox_message")).ShouldBe(outboxBefore);
        (await factory.ScalarAsync<bool>($"SELECT outbox_message_id IS NULL FROM store.notification WHERE template_key = 'otp' AND recipient = '{ScenarioFactory.E164(mobile)}'")).ShouldBeTrue();

        var customer = await factory.CustomerAsync();
        var order = await customer.OrderAsync(Shop.Buy(await factory.ProductIdAsync("letters-tales"), 1, await factory.FormatIdAsync("letters-tales", "print")));
        await factory.SettleAsync(order.Reference);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = 'OrderPaid' AND payload->>'orderId' = '{order.OrderId}' AND processed_at IS NULL")).ShouldBe(1);
        await factory.DrainOutboxAsync();
        (await factory.ScalarAsync<string>($"SELECT string_agg(channel || ':' || template_key, ',' ORDER BY channel) FROM store.notification WHERE related_order_id = '{order.OrderId}'"))
            .ShouldBe("email:order_paid,sms:order_paid");

        var fulfilment = await factory.StaffAsync(Roles.Fulfilment);
        var shipment = await factory.ScalarAsync<Guid>($"SELECT id FROM store.shipment WHERE order_id = '{order.OrderId}'");
        await fulfilment.PostAsync($"/v1/admin/shipments/{shipment}/ship", new { carrier = "SMSA", trackingNo = "T5-63" });
        await factory.DrainOutboxAsync();
        factory.SmsTo(customer.Mobile!).ShouldContain(m => m.Body.Contains("SMSA", StringComparison.Ordinal) || m.Body.Contains("T5-63", StringComparison.Ordinal));
    }
}
