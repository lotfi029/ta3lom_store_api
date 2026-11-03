using System.Net;

using Ta3lom.Store.Api.IntegrationTests.Donations;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Payments;

/// ADR-S05 / plan §3.3–3.4: checkout opens attempt 1 on the gateway; the webhook (verified, re-fetched, idempotent) is the
/// only proof of payment; settlement is exact or goes to review.
public sealed class SettlementTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    [Fact]
    public async Task Checkout_returns_the_hosted_page_and_records_attempt_1()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var payment = checkout.GetProperty("payment");
        payment.Str("redirectUrl").ShouldContain("/dev/payments/fake_inv_");
        payment.GetProperty("expiresAt").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.String);
        (await factory.ScalarAsync<string>($"SELECT p.status || ':' || p.attempt_no || ':' || p.provider || ':' || p.amount FROM store.payment p JOIN store.orders o ON o.id = p.order_id WHERE o.reference = '{reference}'"))
            .ShouldBe("initiated:1:fake:32.00");
        factory.Store().Invoice(PaymentScenario.InvoiceId(checkout))!.ReturnUrl.ShouldContain($"/orders/{reference}?t=");
    }

    [Fact]
    public async Task A_replay_returns_the_same_hosted_page()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();
        await donor.PostAsync("/v1/cart/lines", new { categoryId = c.CategoryId, amount = "50.00" });
        var key = HttpJson.Key();
        var first = await donor.PostAsync("/v1/checkout", new { fullName = "Donor", email = "d@example.com" }, key);
        var replay = await donor.PostAsync("/v1/checkout", new { fullName = "Donor", email = "d@example.com" }, key);
        replay.Response.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        replay.Body.GetProperty("payment").Str("redirectUrl").ShouldBe(first.Body.GetProperty("payment").Str("redirectUrl"));
    }

    [Fact]
    public async Task A1_gateway_down_keeps_the_order_and_a_replay_with_the_same_key_retries_the_gateway()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();
        await donor.PostAsync("/v1/cart/lines", new { categoryId = c.CategoryId, amount = "50.00" });
        var key = HttpJson.Key();
        factory.Store().FailNextCreate = true;

        var first = await donor.PostAsync("/v1/checkout", new { fullName = "Donor", email = "d@example.com" }, key);
        first.Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        first.Body.GetProperty("payment").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        first.Body.Str("paymentError").ShouldBe(ErrorCodes.PaymentGatewayUnavailable);
        first.Body.Str("status").ShouldBe("pending_payment");

        var retry = await donor.PostAsync("/v1/checkout", new { fullName = "Donor", email = "d@example.com" }, key);
        retry.Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        retry.Body.Str("reference").ShouldBe(first.Body.Str("reference"));
        retry.Body.GetProperty("payment").Str("redirectUrl").ShouldContain("/dev/payments/");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.orders WHERE customer_id = '{donor.Id}'")).ShouldBe(1);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.payment p JOIN store.orders o ON o.id = p.order_id WHERE o.customer_id = '{donor.Id}'")).ShouldBe(1);
    }

    [Fact]
    public async Task An_exact_capture_settles_the_order_with_a_DR_receipt_and_queues_OrderPaid()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var body = factory.Act(PaymentScenario.InvoiceId(checkout));

        var hook = await factory.WebhookAsync(body);

        hook.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        hook.Body.Str("outcome").ShouldBe("settled");
        (await factory.OrderStatusAsync(reference)).ShouldBe("paid");
        (await factory.ScalarAsync<string>($"SELECT p.status || ':' || p.captured_amount || ':' || p.method FROM store.payment p JOIN store.orders o ON o.id = p.order_id WHERE o.reference = '{reference}'"))
            .ShouldBe("captured:32.00:mada");
        (await factory.ScalarAsync<string>($"SELECT r.series || ':' || r.amount || ':' || r.donor_name_snapshot FROM store.donation_receipt r JOIN store.orders o ON o.id = r.order_id WHERE o.reference = '{reference}'"))
            .ShouldBe("DR:32.00:عبدالله");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = 'OrderPaid' AND payload->>'reference' = '{reference}'")).ShouldBe(1);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.outbox_message WHERE type = 'ReceiptIssued'")).ShouldBeGreaterThanOrEqualTo(1);
        (await factory.ScalarAsync<bool>("SELECT bool_and(signature_valid) FROM store.payment_event WHERE outcome = 'settled'")).ShouldBeTrue();
        (await factory.ScalarAsync<string>("SELECT payload_json::text FROM store.payment_event WHERE outcome = 'settled' LIMIT 1")).ShouldNotContain("secret_token");
    }

    [Fact]
    public async Task A_replayed_event_is_a_no_op()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var body = factory.Act(PaymentScenario.InvoiceId(checkout));

        (await factory.WebhookAsync(body)).Body.Str("outcome").ShouldBe("settled");
        var again = await factory.WebhookAsync(body);

        again.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        again.Body.Str("outcome").ShouldBe("duplicate");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.donation_receipt r JOIN store.orders o ON o.id = r.order_id WHERE o.reference = '{reference}'")).ShouldBe(1);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = 'OrderPaid' AND payload->>'reference' = '{reference}'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_bad_secret_is_401_and_recorded_unverified()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var body = factory.Act(PaymentScenario.InvoiceId(checkout)).Replace(Ta3lom.Store.Infrastructure.Payments.FakePaymentOptions.DevOnlyWebhookSecret, "forged-secret-0123456789", StringComparison.Ordinal);

        var hook = await factory.WebhookAsync(body);

        hook.Response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        hook.Body.Str("code").ShouldBe(ErrorCodes.PaymentWebhookInvalid);
        (await factory.OrderStatusAsync(checkout.Str("reference"))).ShouldBe("pending_payment");
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.payment_event WHERE NOT signature_valid AND outcome = 'rejected'")).ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Unknown_provider_is_404_and_an_oversized_body_is_413()
    {
        (await factory.WebhookAsync("{}", "paypal")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var big = "{\"pad\":\"" + new string('x', 300 * 1024) + "\"}";
        (await factory.WebhookAsync(big)).Response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task A_valid_but_irrelevant_event_is_200_ignored()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var body = factory.Act(PaymentScenario.InvoiceId(checkout), type: "card_auth_authenticated");
        var hook = await factory.WebhookAsync(body);
        hook.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        hook.Body.Str("outcome").ShouldBe("ignored");
        (await factory.OrderStatusAsync(checkout.Str("reference"))).ShouldBe("pending_payment");
    }

    [Fact]
    public async Task A_wrong_amount_goes_to_payment_review_with_a_finance_alert()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var hook = await factory.WebhookAsync(factory.Act(PaymentScenario.InvoiceId(checkout), amountOverride: 3300));

        hook.Body.Str("outcome").ShouldBe("review");
        (await factory.OrderStatusAsync(reference)).ShouldBe("payment_review");
        (await factory.ScalarAsync<string>($"SELECT review_reason FROM store.orders WHERE reference = '{reference}'")).ShouldBe("amount_mismatch");
        (await factory.ScalarAsync<string>($"SELECT p.status || ':' || p.captured_amount FROM store.payment p JOIN store.orders o ON o.id = p.order_id WHERE o.reference = '{reference}'"))
            .ShouldBe("review:33.00");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = 'PaymentReviewRequired' AND payload->>'reference' = '{reference}'")).ShouldBe(1);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.donation_receipt r JOIN store.orders o ON o.id = r.order_id WHERE o.reference = '{reference}'")).ShouldBe(0);
    }

    [Fact]
    public async Task A_failed_attempt_keeps_the_order_payable_and_pay_opens_attempt_2_which_settles()
    {
        var (donor, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        (await factory.WebhookAsync(factory.Act(PaymentScenario.InvoiceId(checkout), success: false))).Body.Str("outcome").ShouldBe("failed");
        (await factory.OrderStatusAsync(reference)).ShouldBe("pending_payment");

        var pay = await donor.PostAsync($"/v1/orders/{reference}/pay", new { }, HttpJson.Key());
        pay.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = PaymentScenario.InvoiceId(pay.Body);
        second.ShouldNotBe(PaymentScenario.InvoiceId(checkout));
        (await factory.WebhookAsync(factory.Act(second))).Body.Str("outcome").ShouldBe("settled");
        (await factory.OrderStatusAsync(reference)).ShouldBe("paid");
        (await factory.ScalarAsync<string>($"SELECT string_agg(p.attempt_no || ':' || p.status, ',' ORDER BY p.attempt_no) FROM store.payment p JOIN store.orders o ON o.id = p.order_id WHERE o.reference = '{reference}'"))
            .ShouldBe("1:failed,2:captured");
    }

    [Fact]
    public async Task Pay_again_cancels_the_open_attempt_and_works_with_the_status_token()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var token = checkout.Str("statusToken");
        var anonymous = factory.Anonymous();

        var pay = await anonymous.SendJsonAsync(HttpMethod.Post, $"/v1/orders/{reference}/pay?t={Uri.EscapeDataString(token)}", new { },
            new Dictionary<string, string>(HttpJson.Key()) { ["Origin"] = "https://store.ta3lom.sa" });

        pay.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Store().Invoice(PaymentScenario.InvoiceId(checkout))!.Status.ShouldBe("canceled");
        (await factory.ScalarAsync<string>($"SELECT string_agg(p.attempt_no || ':' || p.status, ',' ORDER BY p.attempt_no) FROM store.payment p JOIN store.orders o ON o.id = p.order_id WHERE o.reference = '{reference}'"))
            .ShouldBe("1:cancelled,2:initiated");
        var wrong = await anonymous.SendJsonAsync(HttpMethod.Post, $"/v1/orders/{reference}/pay?t=nope", new { },
            new Dictionary<string, string>(HttpJson.Key()) { ["Origin"] = "https://store.ta3lom.sa" });
        wrong.Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        wrong.Body.Str("code").ShouldBe(ErrorCodes.OrderNotFound);
    }

    [Fact]
    public async Task A_paid_order_is_not_payable_and_a_second_capture_is_a_double_payment_in_review()
    {
        var (donor, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var invoice = PaymentScenario.InvoiceId(checkout);
        (await factory.WebhookAsync(factory.Act(invoice))).Body.Str("outcome").ShouldBe("settled");

        var pay = await donor.PostAsync($"/v1/orders/{reference}/pay", new { }, HttpJson.Key());
        pay.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        pay.Body.Str("code").ShouldBe(ErrorCodes.PaymentNotPayable);

        // the donor pays the same hosted page twice (e.g. two tabs): the order stays paid, the second payment is reviewed
        var second = await factory.WebhookAsync(factory.Act(invoice));
        second.Body.Str("outcome").ShouldBe("review");
        (await factory.OrderStatusAsync(reference)).ShouldBe("paid");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = 'PaymentReviewRequired' AND payload->>'reference' = '{reference}'")).ShouldBe(1);
    }

    [Fact]
    public async Task Out_of_order_a_failed_event_after_a_capture_changes_nothing()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var invoice = PaymentScenario.InvoiceId(checkout);
        var paid = factory.Store().Act(invoice, success: true);
        var paidBody = Ta3lom.Store.Infrastructure.Payments.Fake.FakePaymentStore.WebhookBody(paid, Ta3lom.Store.Infrastructure.Payments.FakePaymentOptions.DevOnlyWebhookSecret, factory.Clock.UtcNow);
        (await factory.WebhookAsync(paidBody)).Body.Str("outcome").ShouldBe("settled");
        // a late "failed" event for the same payment id: the re-fetch says captured → nothing changes
        var failedType = paidBody.Replace("\"payment_paid\"", "\"payment_failed\"", StringComparison.Ordinal)
            .Replace("\"id\":\"fake_evt_", "\"id\":\"fake_evt_late", StringComparison.Ordinal);
        (await factory.WebhookAsync(failedType)).Body.Str("outcome").ShouldBe("already_settled");
        (await factory.OrderStatusAsync(reference)).ShouldBe("paid");

    }

    [Fact]
    public async Task A_review_sends_one_finance_alert_e_mail_without_donor_data()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        await factory.WebhookAsync(factory.Act(PaymentScenario.InvoiceId(checkout), amountOverride: 100));
        await factory.DrainOutboxAsync();
        await factory.DrainOutboxAsync();
        var mails = (await factory.MailAsync()).Where(m => m.Subject.Contains(reference, StringComparison.Ordinal)).ToList();
        mails.ShouldHaveSingleItem().Body.ShouldContain("amount_mismatch");
        mails[0].Body.ShouldNotContain("عبدالله");
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.outbox_message WHERE dead_lettered")).ShouldBe(0);
    }
}
