using Microsoft.Extensions.Logging.Abstractions;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Payments;
using Ta3lom.Store.Infrastructure.Payments.Moyasar;

namespace Ta3lom.Store.Api.IntegrationTests.Payments;

/// Live Moyasar sandbox, only when `MOYASAR_SANDBOX_SECRET_KEY` (sk_test_…) is set — skipped with a visible reason
/// otherwise (A-7; owner step "run the sandbox tests"). Creates a 1.00 SAR invoice, reads it back, cancels it.
public sealed class MoyasarSandboxTests
{
    [Fact]
    public async Task Sandbox_invoice_round_trip()
    {
        var key = Environment.GetEnvironmentVariable("MOYASAR_SANDBOX_SECRET_KEY");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), "MOYASAR_SANDBOX_SECRET_KEY is not set: the live sandbox test is an owner step (PR: owner steps)");
        Assert.SkipUnless(key!.StartsWith("sk_test_", StringComparison.Ordinal), "MOYASAR_SANDBOX_SECRET_KEY must be a test key (sk_test_…)");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var gateway = new MoyasarGateway(http, Microsoft.Extensions.Options.Options.Create(new PaymentOptions
        {
            Provider = "moyasar",
            Moyasar = new MoyasarOptions { SecretKey = key, WebhookSecret = Environment.GetEnvironmentVariable("MOYASAR_WEBHOOK_SECRET") ?? "unused-0123456789" },
        }), NullLogger<MoyasarGateway>.Instance);

        var created = await gateway.CreateHostedPaymentAsync(new HostedPaymentRequest(Guid.NewGuid(), "T3-99-000001", 100, "ta3lom sandbox test",
            "https://store-staging.ta3lom.sa/ar/orders/T3-99-000001", DateTimeOffset.UtcNow.AddHours(1)), TestContext.Current.CancellationToken);
        created.Ok.ShouldBeTrue(created.Error);
        created.RedirectUrl!.ShouldStartWith("https://");
        (await gateway.FindPaymentsForHostedPageAsync(created.ProviderInvoiceId!, TestContext.Current.CancellationToken)).ShouldBeEmpty();
        await gateway.CancelHostedPaymentAsync(created.ProviderInvoiceId!, TestContext.Current.CancellationToken);
    }
}
