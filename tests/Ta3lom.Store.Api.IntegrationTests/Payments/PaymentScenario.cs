using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Api.IntegrationTests.Donations;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Infrastructure.Payments;
using Ta3lom.Store.Infrastructure.Payments.Fake;

namespace Ta3lom.Store.Api.IntegrationTests.Payments;

/// Shared steps of the payment tests: a donor with a cart, checkout through the fake gateway, the donor's action on the
/// fake hosted page, the fake webhook.
internal static class PaymentScenario
{
    public static FakePaymentStore Store(this ScenarioFactory factory) => factory.Services.GetRequiredService<FakePaymentStore>();

    public static async Task<(ActorClient Donor, JsonElement Checkout)> CheckoutAsync(ScenarioFactory factory, string? key = null)
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();
        await donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 3 });
        await donor.PostAsync("/v1/cart/lines", new { categoryId = c.CategoryId, amount = "20.00" });
        var placed = await donor.PostAsync("/v1/checkout", new { fullName = "عبدالله", email = "donor@example.com" }, HttpJson.Key(key));
        placed.Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (donor, placed.Body);
    }

    /// The fake hosted page id from a checkout or pay response (`…/dev/payments/{id}`).
    public static string InvoiceId(JsonElement withPayment) =>
        withPayment.GetProperty("payment").Str("redirectUrl").Split('/')[^1];

    /// The donor acts on the hosted page; returns the webhook body the fake gateway would send.
    public static string Act(this ScenarioFactory factory, string invoiceId, bool success = true, long? amountOverride = null, string? type = null)
    {
        var payment = factory.Store().Act(invoiceId, success, amountOverride);
        return FakePaymentStore.WebhookBody(payment, FakePaymentOptions.DevOnlyWebhookSecret, factory.Clock.UtcNow, type);
    }

    public static async Task<(HttpResponseMessage Response, JsonElement Body)> WebhookAsync(this ScenarioFactory factory, string body, string provider = "fake")
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await factory.Anonymous().PostAsync($"/v1/payments/webhooks/{provider}", content, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response, text.StartsWith('{') ? JsonDocument.Parse(text).RootElement.Clone() : default);
    }

    public static Task<string> OrderStatusAsync(this ScenarioFactory factory, string reference) =>
        factory.ScalarAsync<string>($"SELECT status FROM store.orders WHERE reference = '{reference}'");
}
