using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Ta3lom.Store.Infrastructure.Payments;
using Ta3lom.Store.Infrastructure.Services.StoreMode;

namespace Ta3lom.Store.Api.IntegrationTests.Payments;

/// C-2 / plan §3.2: start-up validation of `Store:Payments`.
public sealed class PaymentOptionsTests
{
    private static bool Valid(string environment, PaymentOptions options) =>
        new PaymentOptionsValidator(new Env(environment)).Validate(null, options).Succeeded;

    private static PaymentOptions Moyasar(string key, string secret = "whsec_0123456789abcdef") =>
        new() { Provider = "moyasar", Moyasar = new MoyasarOptions { SecretKey = key, WebhookSecret = secret } };

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public void The_fake_gateway_exists_only_in_Development_and_tests(string environment, bool valid) =>
        Valid(environment, new PaymentOptions { Provider = "fake" }).ShouldBe(valid);

    [Theory]
    [InlineData("Production", "sk_live_abc", true)]
    [InlineData("Production", "sk_test_abc", false)]
    [InlineData("Staging", "sk_test_abc", true)]
    [InlineData("Staging", "sk_live_abc", false)]
    [InlineData("Production", "", false)]
    public void Moyasar_keys_match_the_environment(string environment, string key, bool valid) =>
        Valid(environment, Moyasar(key)).ShouldBe(valid);

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("DEV ONLY webhook secret value")]
    public void The_webhook_secret_is_required_and_not_a_dev_value(string secret) =>
        Valid("Production", Moyasar("sk_live_abc", secret)).ShouldBeFalse();

    [Fact]
    public void Fake_dev_secret_is_refused_for_Moyasar_and_unknown_providers_fail() =>
        (Valid("Staging", Moyasar("sk_test_abc", FakePaymentOptions.DevOnlyWebhookSecret)) || Valid("Development", new PaymentOptions { Provider = "paypal" }))
            .ShouldBeFalse();

    /// SM-1: showcase — `none` is accepted in every environment and the (absent) Moyasar settings are not validated.
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Provider_none_is_valid_everywhere_without_Moyasar_settings(string environment)
    {
        var options = new PaymentOptions { Provider = "None ", Moyasar = new MoyasarOptions { SecretKey = "", WebhookSecret = "", BaseUrl = "" } };

        Valid(environment, options).ShouldBeTrue();
        options.PaymentsEnabled.ShouldBeFalse();
        new PaymentOptions { Provider = "moyasar" }.PaymentsEnabled.ShouldBeTrue();
    }

    /// SM-4: donor sign-in off is only allowed without payments (checkout needs a signed-in donor); sign-in on without
    /// payments is allowed (REVIEW).
    [Theory]
    [InlineData("none", false, true)]
    [InlineData("none", true, true)]
    [InlineData("moyasar", true, true)]
    [InlineData("moyasar", false, false)]
    [InlineData("fake", false, false)]
    public void Sign_in_off_requires_payments_off(string provider, bool customerSignIn, bool valid) =>
        new StoreFeatureOptionsValidator(Microsoft.Extensions.Options.Options.Create(new PaymentOptions { Provider = provider }))
            .Validate(null, new StoreFeatureOptions { CustomerSignIn = customerSignIn }).Succeeded.ShouldBe(valid);

    /// SM-4: Production requires unifonic + AppSid + SenderId only while donors sign in by SMS.
    [Theory]
    [InlineData("Production", true, false)]
    [InlineData("Production", false, true)]
    [InlineData("Staging", true, true)]
    public void Sms_is_required_in_Production_only_with_customer_sign_in(string environment, bool customerSignIn, bool valid) =>
        new Ta3lom.Store.Infrastructure.Services.Sms.SmsOptionsValidator(new Env(environment),
                Microsoft.Extensions.Options.Options.Create(new StoreFeatureOptions { CustomerSignIn = customerSignIn }))
            .Validate(null, new Ta3lom.Store.Infrastructure.Services.Sms.SmsOptions { Provider = "unifonic" }).Succeeded.ShouldBe(valid);

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = "/";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
