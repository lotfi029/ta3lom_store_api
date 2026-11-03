using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Ta3lom.Store.Infrastructure.Payments;

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

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = "/";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
