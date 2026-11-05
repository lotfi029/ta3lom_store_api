using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Showcase;

/// Showcase mode (SM-1 … SM-6): the store runs without a payment provider and without donor sign-in. Staff admin works;
/// every route that would create an order, a cart, a payment or a donor session answers 409 before anything is written.
public sealed class ShowcaseModeTests(ShowcaseScenarioFactory factory) : IClassFixture<ShowcaseScenarioFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static readonly string[] PaymentJobs = ["ReconcilePaymentsJob", "ExpireUnpaidOrdersJob", "ReconcileRefundsJob"];

    public static readonly string[] OtherJobs = ["PruneOtpJob", "PruneIdempotencyKeysJob", "ExpireGuestCartsJob", "RetentionJob", "IntegrityChecksJob"];

    private static async Task ShouldBeDisabledAsync(HttpResponseMessage response, string code)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync(Ct));
        (await response.ReadProblemAsync()).Str("code").ShouldBe(code);
    }

    // ── SM-1 ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SM1_the_host_starts_with_no_default_gateway_and_logs_one_showcase_warning()
    {
        factory.CreateClient().Dispose();
        await using var scope = factory.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IPaymentGatewayRegistry>();

        Should.Throw<InvalidOperationException>(() => registry.Default).Message.ShouldContain("payments disabled");
        registry.Find("moyasar").ShouldBeNull();
        var warnings = factory.Sink.Events.Where(e => e.Level == LogEventLevel.Warning
            && e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("payments disabled (showcase)", StringComparison.Ordinal));
        warnings.Count().ShouldBe(1);
    }

    // ── SM-2 ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SM2_checkout_is_409_before_anything_is_written_and_a_retry_is_not_a_replay()
    {
        var donor = await factory.CustomerAsync();
        var headers = new Dictionary<string, string> { ["Idempotency-Key"] = Guid.NewGuid().ToString() };

        var (first, _) = await donor.SendAsync(HttpMethod.Post, "/v1/checkout", new { fullName = "متبرع", locale = "ar" }, headers);
        var (second, _) = await donor.SendAsync(HttpMethod.Post, "/v1/checkout", new { fullName = "متبرع", locale = "ar" }, headers);

        first.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.StorePaymentsDisabled);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.orders")).ShouldBe(0);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.idempotency_key")).ShouldBe(0);
    }

    [Fact]
    public async Task SM2_pay_is_409_and_writes_no_idempotency_record()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/orders/T3-00-000000/pay");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        await ShouldBeDisabledAsync(await factory.Anonymous().SendAsync(request, Ct), ErrorCodes.StorePaymentsDisabled);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.idempotency_key")).ShouldBe(0);
    }

    [Fact]
    public async Task SM2_adding_a_cart_line_is_409_creates_no_cart_and_sets_no_cookie()
    {
        var browser = factory.Browser();
        var projectId = Guid.NewGuid();                                     // refused before any lookup

        var response = await browser.PostAsJsonAsync("/v1/cart/lines", new { projectId, amount = "50.00" }, Ct);

        await ShouldBeDisabledAsync(response, ErrorCodes.StorePaymentsDisabled);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.cart")).ShouldBe(0);
    }

    [Fact]
    public async Task SM2_the_cart_summary_badge_keeps_answering_with_an_empty_cart()
    {
        var response = await factory.Browser().GetAsync("/v1/cart/summary", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("lineCount").GetInt32().ShouldBe(0);
    }

    [Theory]
    [InlineData("/v1/admin/dashboard")]
    [InlineData("/v1/admin/orders")]
    [InlineData("/v1/admin/reports/donations.csv")]
    [InlineData("/v1/admin/payments/orphans")]
    [InlineData("/v1/admin/customers")]
    public async Task SM2_staff_read_views_keep_working(string path)
    {
        var finance = await factory.StaffAsync(Roles.Admin);

        var response = await finance.Http.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    // ── SM-3 ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SM3_payment_jobs_are_not_scheduled_and_the_others_are()
    {
        factory.CreateClient().Dispose();
        var jobs = factory.Services.GetRequiredService<IJobRunner>().Jobs.Keys.ToList();

        jobs.ShouldBe(OtherJobs, ignoreOrder: true);
    }

    [Fact]
    public async Task SM3_the_moyasar_webhook_is_an_unknown_provider()
    {
        using var content = new StringContent("""{"id":"evt_1","type":"payment_paid"}""", System.Text.Encoding.UTF8, "application/json");

        var response = await factory.Anonymous().PostAsync("/v1/payments/webhooks/moyasar", content, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.PaymentUnknownProvider);
    }

    // ── SM-4 ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/v1/auth/otp/request")]
    [InlineData("/v1/auth/otp/verify")]
    [InlineData("/v1/auth/refresh")]
    public async Task SM4_donor_sign_in_routes_are_409(string path)
    {
        var response = await factory.Browser().PostAsJsonAsync(path, new { mobile = "0590000001", code = "123456" }, Ct);

        await ShouldBeDisabledAsync(response, ErrorCodes.StoreSignInDisabled);
        factory.Sms.Latest(50).ShouldBeEmpty();
    }

    [Fact]
    public async Task SM4_staff_sign_in_is_untouched()
    {
        var response = await factory.Browser().PostAsJsonAsync("/v1/admin/auth/login", new { email = "nobody@test.ta3lom.sa", password = "wrong-password-123" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.InvalidCredentials);
    }

    // ── SM-5 ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SM5_the_public_config_says_payments_and_sign_in_are_off()
    {
        var body = await factory.Anonymous().GetFromJsonAsync<JsonElement>("/v1/catalog/config", Ct);

        body.GetProperty("paymentsEnabled").GetBoolean().ShouldBeFalse();
        body.GetProperty("customerSignIn").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task SM5_readiness_reports_the_showcase_mode()
    {
        var body = await factory.Anonymous().GetFromJsonAsync<JsonElement>("/health/ready", Ct);

        var mode = body.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "mode");
        mode.GetProperty("data").GetProperty("mode").GetString().ShouldBe("showcase");
    }

    // ── SM-6 ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ar", "/v1/cart/lines", "التبرع الإلكتروني غير متاح بعد")]
    [InlineData("en", "/v1/cart/lines", "Online donations are not open yet")]
    [InlineData("ar", "/v1/auth/otp/request", "تسجيل دخول المتبرعين غير متاح بعد")]
    [InlineData("en", "/v1/auth/otp/request", "Donor sign-in is not open yet")]
    public async Task SM6_the_problem_title_is_localised(string language, string path, string title)
    {
        var browser = factory.Browser();
        browser.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);

        var response = await browser.PostAsJsonAsync(path, new { amount = "50.00", mobile = "0590000001" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Str("title").ShouldBe(title);
    }
}

/// The same probes in live mode (the default test host: fake gateway, sign-in on).
public sealed class LiveModeTests(StoreApiFactory factory) : IClassFixture<StoreApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SM5_the_public_config_says_payments_and_sign_in_are_on()
    {
        var body = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/v1/catalog/config", Ct);

        body.GetProperty("paymentsEnabled").GetBoolean().ShouldBeTrue();
        body.GetProperty("customerSignIn").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task SM5_readiness_reports_the_live_mode()
    {
        var body = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/health/ready", Ct);

        var mode = body.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "mode");
        mode.GetProperty("data").GetProperty("mode").GetString().ShouldBe("live");
    }

    [Fact]
    public void SM3_every_job_is_scheduled_in_live_mode()
    {
        factory.CreateClient().Dispose();
        var jobs = factory.Services.GetRequiredService<IJobRunner>().Jobs.Keys.ToList();

        jobs.ShouldBe([.. ShowcaseModeTests.OtherJobs, .. ShowcaseModeTests.PaymentJobs], ignoreOrder: true);
    }
}

/// SM-1 / SM-4: Production starts in showcase with no Moyasar and no SMS settings at all.
public sealed class ShowcaseProductionTests
{
    [Fact]
    public async Task Production_starts_in_showcase_without_Moyasar_or_SMS_secrets()
    {
        await using var factory = new ShowcaseProductionFactory();

        var response = await factory.CreateClient().GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("\"mode\":\"showcase\"");
    }
}
