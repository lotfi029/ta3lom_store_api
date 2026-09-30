using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.Scenarios;

/// T-7 — success paths of the staff session (#47 … #52), staff management (#123 … #128), the customer profile and addresses
/// (#21 … #27), cart edits (#13 … #15), customer logout (#19, #20) and the order status / cancel / resend routes (#42, #87, #88).
public sealed class AccountAndStaffFlowsTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Totp(string secret) => new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(secret)).ComputeTotp();

    [Fact]
    [Rule("RUL-62")]
    public async Task A_staff_member_logs_in_with_password_and_TOTP_refreshes_and_logs_out()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        var email = $"flow-{Guid.NewGuid():N}@test.ta3lom.sa";
        var (_, created) = await admin.PostAsync("/v1/admin/staff", new { email, name = "Flow", roles = new[] { "store_manager" } });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        client.DefaultRequestHeaders.Add(RemoteIpStartupFilter.Header, "198.51.100.77");

        var login = await (await client.PostAsJsonAsync("/v1/admin/auth/login", new { email, password = created.GetProperty("oneTimePassword").GetString() }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);
        login.GetProperty("totpEnrolled").GetBoolean().ShouldBeFalse();
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("mfaToken").GetString());
        var enrol = await (await client.PostAsync("/v1/admin/auth/totp/enrol", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        enrol.GetProperty("otpauthUri").GetString()!.ShouldStartWith("otpauth://totp/");
        var wrong = await client.PostAsJsonAsync("/v1/admin/auth/totp/verify", new { code = "000000" }, Ct);
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var verified = await client.PostAsJsonAsync("/v1/admin/auth/totp/verify", new { code = Totp(enrol.GetProperty("secretBase32").GetString()!) }, Ct);
        verified.StatusCode.ShouldBe(HttpStatusCode.OK);
        var session = await verified.Content.ReadFromJsonAsync<JsonElement>(Ct);
        session.GetProperty("expiresIn").GetInt32().ShouldBe(600);
        var cookie = verified.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("t3s_srt=", StringComparison.Ordinal));
        cookie.ShouldContain("path=/v1/admin/auth", Case.Insensitive);

        client.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        (await client.GetAsync("/v1/admin/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/auth/refresh");
        refresh.Headers.Add("Cookie", cookie.Split(';')[0]);
        var refreshed = await client.SendAsync(refresh, Ct);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotated = refreshed.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("t3s_srt=", StringComparison.Ordinal)).Split(';')[0];
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/auth/logout");
        logout.Headers.Add("Cookie", rotated);
        (await client.SendAsync(logout, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var after = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/auth/refresh");
        after.Headers.Add("Cookie", rotated);
        (await client.SendAsync(after, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Rule("RUL-62")]
    public async Task Admins_manage_staff_roles_passwords_TOTP_and_locks()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        var (created, body) = await admin.PostAsync("/v1/admin/staff", new { email = $"m-{Guid.NewGuid():N}@test.ta3lom.sa", name = "Managed", roles = new[] { "viewer" } });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = body.GetProperty("staff").GetProperty("id").GetGuid();

        (await admin.PostAsync("/v1/admin/staff", new { email = body.GetProperty("staff").GetProperty("email").GetString(), name = "Dup", roles = new[] { "viewer" } }))
            .Body.Code().ShouldBe("staff.email_taken");
        (await admin.PostAsync("/v1/admin/staff", new { email = "x@test.ta3lom.sa", name = "Bad", roles = new[] { "superuser" } })).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var (updated, u) = await admin.SendAsync(HttpMethod.Put, $"/v1/admin/staff/{id}", new { name = "Managed 2", roles = new[] { "finance", "viewer" }, isActive = true });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        u.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldBe(["finance", "viewer"], ignoreOrder: true);
        var (reset, pw) = await admin.PostAsync($"/v1/admin/staff/{id}/reset-password");
        reset.StatusCode.ShouldBe(HttpStatusCode.OK);
        pw.GetProperty("oneTimePassword").GetString()!.Length.ShouldBeGreaterThanOrEqualTo(12);
        (await admin.PostAsync($"/v1/admin/staff/{id}/reset-totp")).Response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PostAsync($"/v1/admin/staff/{id}/unlock")).Response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var (_, list) = await admin.GetAsync("/v1/admin/staff");
        list.EnumerateArray().ShouldContain(s => s.GetProperty("id").GetGuid() == id);
        (await admin.PostAsync($"/v1/admin/staff/{Guid.NewGuid()}/unlock")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Rule("RUL-61")]
    public async Task A_customer_keeps_a_profile_and_saved_addresses_with_one_default()
    {
        var customer = await factory.CustomerAsync();
        (await customer.SendAsync(HttpMethod.Patch, "/v1/me", new { fullName = "هند", email = "hind@example.com", locale = "en", anonymousDefault = true, pdplConsent = true }))
            .Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await customer.SendAsync(HttpMethod.Patch, "/v1/me", new { locale = "fr" })).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var (_, me) = await customer.GetAsync("/v1/me");
        me.GetProperty("locale").GetString().ShouldBe("en");
        me.GetProperty("anonymousDefault").GetBoolean().ShouldBeTrue();

        object Address(string city) => new { recipientName = "هند", recipientMobile = "0551234567", city, district = "النخيل", street = "شارع ١", buildingNo = "1234", postalCode = "12345" };
        var (a1r, a1) = await customer.PostAsync("/v1/me/addresses", Address("الرياض"));
        a1r.StatusCode.ShouldBe(HttpStatusCode.Created);
        a1.GetProperty("isDefault").GetBoolean().ShouldBeTrue();                                        // the first address is the default
        var (_, a2) = await customer.PostAsync("/v1/me/addresses", Address("جدة"));
        a2.GetProperty("isDefault").GetBoolean().ShouldBeFalse();
        var a2Id = a2.GetProperty("id").GetGuid();
        (await customer.SendAsync(HttpMethod.Put, $"/v1/me/addresses/{a2Id}", Address("مكة"))).Body.GetProperty("city").GetString().ShouldBe("مكة");
        (await customer.PostAsync($"/v1/me/addresses/{a2Id}/default")).Body.GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        (await customer.SendAsync(HttpMethod.Delete, $"/v1/me/addresses/{a2Id}")).Response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var (_, list) = await customer.GetAsync("/v1/me/addresses");
        list.GetArrayLength().ShouldBe(1);
        list[0].GetProperty("isDefault").GetBoolean().ShouldBeTrue();                                   // the remaining one takes over
        (await (await factory.CustomerAsync()).SendAsync(HttpMethod.Delete, $"/v1/me/addresses/{a1.GetProperty("id").GetGuid()}")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Rule("RUL-12")]
    public async Task Cart_lines_can_be_changed_removed_and_the_cart_emptied()
    {
        var customer = await factory.CustomerAsync();
        var print = await factory.FormatIdAsync("letters-tales", "print");
        var (_, added) = await customer.PostAsync("/v1/cart/lines", Shop.Buy(await factory.ProductIdAsync("letters-tales"), 2, print));
        var lineId = added.GetProperty("purchases")[0].GetProperty("id").GetGuid();
        await customer.PostAsync("/v1/cart/lines", Shop.Donate(await factory.ProductIdAsync("school-bag")));

        (await customer.SendAsync(HttpMethod.Patch, $"/v1/cart/lines/{lineId}", new { quantity = 7 })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await customer.SendAsync(HttpMethod.Patch, $"/v1/cart/lines/{lineId}", new { quantity = 21 })).Body.Code().ShouldBe("cart.quantity_limit");
        var (_, badge) = await customer.GetAsync("/v1/cart/summary");
        badge.GetProperty("lineCount").GetInt32().ShouldBe(2);
        (await customer.SendAsync(HttpMethod.Delete, $"/v1/cart/lines/{lineId}")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await customer.SendAsync(HttpMethod.Delete, $"/v1/cart/lines/{lineId}")).Body.Code().ShouldBe("cart.line_not_found");
        (await customer.SendAsync(HttpMethod.Delete, "/v1/cart")).Response.IsSuccessStatusCode.ShouldBeTrue();
        (await customer.GetAsync("/v1/cart/summary")).Body.GetProperty("lineCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    [Rule("RUL-60")]
    public async Task A_customer_logs_out_here_or_everywhere()
    {
        var browser = factory.Browser();
        var mobile = ScenarioFactory.NewMobile();
        await factory.SignInAsync(browser, mobile);

        (await browser.PostAsync("/v1/auth/refresh", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await browser.PostAsync("/v1/auth/logout", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await browser.PostAsync("/v1/auth/refresh", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var other = factory.Browser();
        await factory.SignInAsync(other, mobile);
        (await other.PostAsync("/v1/auth/logout-all", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await other.PostAsync("/v1/auth/refresh", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Rule("RUL-29")]
    public async Task Order_status_with_bearer_or_token_staff_cancel_and_document_resend()
    {
        var customer = await factory.CustomerAsync();
        await customer.PostAsync("/v1/cart/lines", Shop.Donate(await factory.ProductIdAsync("water-for-schools")));
        var (_, quote) = await customer.PostAsync("/v1/checkout/quote");
        var (_, placed) = await customer.PostAsync("/v1/checkout", new
        {
            contact = new { fullName = "عميل", email = "c@example.com" },
            isAnonymous = false,
            saveAddress = false,
            paymentMethod = "mada",
            acceptTerms = true,
            expectedGrandTotal = quote.GetProperty("grandTotal").GetString(),
            locale = "ar",
        }, new Dictionary<string, string> { ["Idempotency-Key"] = Guid.NewGuid().ToString() });
        var reference = placed.GetProperty("reference").GetString()!;
        var token = placed.GetProperty("statusToken").GetString()!;

        (await customer.GetAsync($"/v1/orders/{reference}/status")).Body.GetProperty("status").GetString().ShouldBe("pending_payment");
        var anonymous = new ActorClient(factory.Anonymous(), Guid.Empty, null, null);
        (await anonymous.GetAsync($"/v1/orders/{reference}/status?t={token}")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync($"/v1/orders/{reference}/status")).Response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/v1/orders/{reference}/status?t=st_wrong")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var finance = await factory.StaffAsync(Roles.Finance, Roles.Admin);
        (await finance.PostAsync($"/v1/admin/orders/{reference}/cancel", new { reason = "طلب العميل" })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await finance.PostAsync($"/v1/admin/orders/{reference}/cancel", new { reason = "مرة ثانية" })).Response.IsSuccessStatusCode.ShouldBeFalse();
        (await customer.GetAsync($"/v1/orders/{reference}/status")).Body.GetProperty("status").GetString().ShouldBe("cancelled");

        var paid = await factory.PaidOrderAsync(customer, Shop.Donate(await factory.ProductIdAsync("water-for-schools")));
        var (resent, _) = await finance.PostAsync($"/v1/admin/orders/{paid.Reference}/resend-documents", new { channels = new[] { "email", "sms" } });
        resent.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await finance.PostAsync($"/v1/admin/orders/{reference}/resend-documents", new { channels = new[] { "email" } })).Response.IsSuccessStatusCode.ShouldBeFalse();
        var (_, adminView) = await finance.GetAsync($"/v1/admin/orders/{paid.Reference}");
        adminView.GetProperty("documents").GetArrayLength().ShouldBe(1);
        (await finance.GetAsync($"/v1/admin/orders?q={paid.Reference}")).Body.GetProperty("items").GetArrayLength().ShouldBe(1);
    }
}
