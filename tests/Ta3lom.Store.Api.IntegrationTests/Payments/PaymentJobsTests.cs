using System.Net;
using Ta3lom.Store.Api.IntegrationTests.Donations;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Features.Payments;

namespace Ta3lom.Store.Api.IntegrationTests.Payments;

/// RUL-38 reconciliation, expiry, late captures, and the order status fields (plan §3.3–3.4).
public sealed class PaymentJobsTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>, IDisposable
{
    private readonly DateTimeOffset _start = factory.Clock.UtcNow;

    /// The class shares one clock: a test that moves it puts it back, so later tokens are not issued "in the future".
    public void Dispose() => factory.Clock.Set(_start);

    private Task<SharedKernel.Result<int>> ReconcileAsync() => factory.RunAsync<ReconcilePaymentsCommand, int>(new ReconcilePaymentsCommand());

    private Task<SharedKernel.Result<int>> ExpireAsync() => factory.RunAsync<ExpireUnpaidOrdersCommand, int>(new ExpireUnpaidOrdersCommand());

    [Fact]
    public async Task Reconciliation_settles_a_payment_whose_webhook_never_came()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        factory.Store().Act(PaymentScenario.InvoiceId(checkout), success: true);         // paid on the hosted page, webhook lost

        await ReconcileAsync();                                            // younger than 5 minutes: not yet
        (await factory.OrderStatusAsync(reference)).ShouldBe("pending_payment");
        factory.Clock.Advance(TimeSpan.FromMinutes(6));
        (await ReconcileAsync()).Value.ShouldBeGreaterThanOrEqualTo(1);

        (await factory.OrderStatusAsync(reference)).ShouldBe("paid");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.donation_receipt r JOIN store.orders o ON o.id = r.order_id WHERE o.reference = '{reference}'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_webhook_and_the_reconciliation_at_the_same_time_settle_once()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var body = factory.Act(PaymentScenario.InvoiceId(checkout));
        factory.Clock.Advance(TimeSpan.FromMinutes(6));

        await Task.WhenAll(factory.WebhookAsync(body), ReconcileAsync(), ReconcileAsync(), factory.WebhookAsync(body));

        (await factory.OrderStatusAsync(reference)).ShouldBe("paid");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.donation_receipt r JOIN store.orders o ON o.id = r.order_id WHERE o.reference = '{reference}'")).ShouldBe(1);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = 'OrderPaid' AND payload->>'reference' = '{reference}'")).ShouldBe(1);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.payment p JOIN store.orders o ON o.id = p.order_id WHERE o.reference = '{reference}'")).ShouldBe(1);
    }

    [Fact]
    public async Task An_unpaid_order_expires_after_its_due_time_and_its_page_is_cancelled()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var invoice = PaymentScenario.InvoiceId(checkout);

        factory.Clock.Advance(TimeSpan.FromMinutes(61));
        (await ExpireAsync()).Value.ShouldBeGreaterThanOrEqualTo(1);

        (await factory.OrderStatusAsync(reference)).ShouldBe("expired");
        (await factory.ScalarAsync<string>($"SELECT p.status FROM store.payment p JOIN store.orders o ON o.id = p.order_id WHERE o.reference = '{reference}'")).ShouldBe("cancelled");
        factory.Store().Invoice(invoice)!.Status.ShouldBe("canceled");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = 'OrderExpired' AND payload->>'reference' = '{reference}'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_capture_made_before_the_due_time_settles_instead_of_expiring()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        factory.Store().Act(PaymentScenario.InvoiceId(checkout), success: true);         // paid, webhook lost
        factory.Clock.Advance(TimeSpan.FromMinutes(61));

        await ExpireAsync();

        (await factory.OrderStatusAsync(reference)).ShouldBe("paid");
    }

    [Fact]
    public async Task A_capture_after_expiry_goes_to_review_as_late_capture()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var invoice = PaymentScenario.InvoiceId(checkout);
        factory.Clock.Advance(TimeSpan.FromMinutes(61));
        await ExpireAsync();

        var hook = await factory.WebhookAsync(factory.Act(invoice));

        hook.Body.Str("outcome").ShouldBe("review");
        (await factory.OrderStatusAsync(reference)).ShouldBe("payment_review");
        (await factory.ScalarAsync<string>($"SELECT review_reason FROM store.orders WHERE reference = '{reference}'")).ShouldBe("late_capture");
    }

    [Fact]
    public async Task The_status_route_reports_payment_state_retry_and_receipt()
    {
        var (donor, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var url = $"/v1/orders/{reference}/status?t={Uri.EscapeDataString(checkout.Str("statusToken"))}";
        var anonymous = factory.Anonymous();

        var pending = await anonymous.SendJsonAsync(HttpMethod.Get, url);
        pending.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        pending.Body.Str("paymentStatus").ShouldBe("initiated");
        pending.Body.GetProperty("canRetryPayment").GetBoolean().ShouldBeFalse();
        pending.Body.GetProperty("receiptAvailable").GetBoolean().ShouldBeFalse();
        pending.Body.Str("refundedAmount").ShouldBe("0.00");

        await factory.WebhookAsync(factory.Act(PaymentScenario.InvoiceId(checkout), success: false));
        var failed = await donor.GetAsync($"/v1/orders/{reference}/status");
        failed.Body.Str("paymentStatus").ShouldBe("failed");
        failed.Body.GetProperty("canRetryPayment").GetBoolean().ShouldBeTrue();

        var pay = await donor.PostAsync($"/v1/orders/{reference}/pay", new { }, HttpJson.Key());
        await factory.WebhookAsync(factory.Act(PaymentScenario.InvoiceId(pay.Body)));
        var paid = await anonymous.SendJsonAsync(HttpMethod.Get, url);
        paid.Body.Str("status").ShouldBe("paid");
        paid.Body.Str("paymentStatus").ShouldBe("captured");
        paid.Body.GetProperty("canRetryPayment").GetBoolean().ShouldBeFalse();
        paid.Body.GetProperty("receiptAvailable").GetBoolean().ShouldBeTrue();
        paid.Body.Str("receiptNumber").ShouldMatch(@"^DR-\d{4}-\d{6}$");
        paid.Body.GetProperty("paidAt").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.String);
    }

    [Fact]
    public async Task The_fake_hosted_page_and_clock_are_not_mapped_outside_Development()
    {
        (await factory.Anonymous().GetAsync("/dev/payments/fake_inv_x", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.Anonymous().GetAsync("/dev/clock", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
