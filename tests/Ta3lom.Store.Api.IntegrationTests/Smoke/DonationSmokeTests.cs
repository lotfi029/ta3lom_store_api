using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Ta3lom.Store.Api.IntegrationTests.Donations;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Infrastructure.Persistence.Seeders;

namespace Ta3lom.Store.Api.IntegrationTests.Smoke;

/// The release smoke run, end to end through the API: seed → guest cart (option, project amount, category amount) → OTP
/// sign-in with the code from the in-process SMS sink → cart merge → quote → checkout and its replay → status page →
/// staff sign-in with TOTP → forced password change → cancel the order → audit rows.
public sealed class DonationSmokeTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    [Fact]
    public async Task A_donation_goes_from_guest_cart_to_a_cancelled_order()
    {
        // Seed the demo catalogue.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DevelopmentCatalogSeeder>().RunAsync(Ct);
        }

        var guest = factory.Browser();
        var project = (await guest.SendJsonAsync(HttpMethod.Get, "/v1/catalog/projects/print-a-title")).Body;
        var category = (await guest.SendJsonAsync(HttpMethod.Get, "/v1/catalog/categories")).Body.EnumerateArray().First(c => c.GetProperty("acceptsGeneralGifts").GetBoolean());
        var option = project.GetProperty("options")[0];

        // Guest cart: three kinds of line.
        (await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = project.Str("id"), optionId = option.Str("id"), quantity = 2 }))
            .Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { projectId = project.Str("id"), amount = "50.00" })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var guestCart = (await guest.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", new { categoryId = category.Str("id"), amount = "30.00" })).Body;
        guestCart.GetProperty("lineCount").GetInt32().ShouldBe(3);

        // OTP sign-in (code from the SMS sink) merges the guest cart.
        var mobile = ScenarioFactory.NewMobile();
        var session = await factory.SignInAsync(guest, mobile);
        session.GetProperty("customer").GetProperty("isNew").GetBoolean().ShouldBeTrue();
        var cart = (await guest.SendJsonAsync(HttpMethod.Get, "/v1/cart")).Body;
        cart.GetProperty("lineCount").GetInt32().ShouldBe(3);
        cart.Str("totalAmount").ShouldBe(guestCart.Str("totalAmount"));

        // Quote, then checkout and its replay.
        var quote = (await guest.SendJsonAsync(HttpMethod.Post, "/v1/checkout/quote")).Body;
        quote.GetProperty("problems").GetArrayLength().ShouldBe(0);
        var key = HttpJson.Key();
        var body = new { fullName = "متبرع", email = "smoke@example.com" };
        var placed = await guest.SendJsonAsync(HttpMethod.Post, "/v1/checkout", body, key);
        placed.Response.StatusCode.ShouldBe(HttpStatusCode.Created, placed.Body.ToString());
        var replay = await guest.SendJsonAsync(HttpMethod.Post, "/v1/checkout", body, key);
        replay.Body.Str("reference").ShouldBe(placed.Body.Str("reference"));
        var reference = placed.Body.Str("reference");
        placed.Body.Str("totalAmount").ShouldBe(quote.Str("totalAmount"));

        // Status page with the token, no session.
        var status = await factory.Anonymous().SendJsonAsync(HttpMethod.Get, $"/v1/orders/{reference}/status?t={placed.Body.Str("statusToken")}");
        status.Body.Str("status").ShouldBe("pending_payment");

        // Staff: password → TOTP → forced password change → cancel.
        var (_, email, oneTime) = await factory.NewStaffAccountAsync(Roles.Admin);
        var staff = factory.Browser();
        var mfa = (await JsonAsync(await staff.PostAsJsonAsync("/v1/admin/auth/login", new { email, password = oneTime }, Ct))).Str("mfaToken");
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mfa);
        var secret = (await JsonAsync(await staff.PostAsync("/v1/admin/auth/totp/enrol", null, Ct))).Str("secretBase32");
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(DateTime.UtcNow);
        var verified = await staff.PostAsJsonAsync("/v1/admin/auth/totp/verify", new { code }, Ct);
        verified.StatusCode.ShouldBe(HttpStatusCode.OK);
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await JsonAsync(verified)).Str("accessToken"));
        (await staff.PostAsync($"/v1/admin/orders/{reference}/cancel", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var changed = await staff.PostAsJsonAsync("/v1/admin/auth/password", new { currentPassword = oneTime, newPassword = "Smoke-Test-Passphrase-2026" }, Ct);
        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await JsonAsync(changed)).Str("accessToken"));
        var cancelled = await staff.PostAsync($"/v1/admin/orders/{reference}/cancel", null, Ct);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(cancelled)).Str("status").ShouldBe("cancelled");
        (await factory.Anonymous().SendJsonAsync(HttpMethod.Get, $"/v1/orders/{reference}/status?t={placed.Body.Str("statusToken")}")).Body.Str("status").ShouldBe("cancelled");

        // Audit and outbox.
        foreach (var action in new[] { "staff.login_succeeded", "staff.password_changed", "order.cancelled" })
        {
            (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = '{action}' AND actor = '{email}'")).ShouldBeGreaterThanOrEqualTo(1, action);
        }

        await factory.DrainOutboxAsync();
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.outbox_message WHERE processed_at IS NULL")).ShouldBe(0);
        (await factory.MailAsync()).Count(m => m.Subject.Contains(reference, StringComparison.Ordinal)).ShouldBe(0);   // D-a: logged only
        (await factory.IntegrityFailuresAsync()).ShouldBeEmpty();
    }
}
