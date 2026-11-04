using System.Net;
using System.Text.Json;
using Ta3lom.Store.Api.IntegrationTests.Donations;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Features.Payments;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;
using Ta3lom.Store.Infrastructure.Payments;
using Ta3lom.Store.Infrastructure.Payments.Fake;

namespace Ta3lom.Store.Api.IntegrationTests.Payments;

/// Review 04 (payments hardening): refunded/voided gateway states (A-1), refunds made on the gateway's dashboard (A-2),
/// orphan payments (A-3), one live invoice (A-4/P-5), reconciliation of every invoice (P-6/A-5), pending refunds (P-3/A-6),
/// the refundable bound under the lock (P-4), expiry when the gateway is down (P-8), partial review accept (P-9) and a
/// missing currency (S-2).
public sealed class HardeningTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>, IDisposable
{
    private readonly DateTimeOffset _start = factory.Clock.UtcNow;

    public void Dispose()
    {
        factory.Clock.Set(_start);
        var store = factory.Store();
        store.FailReads = false;
        store.PendingRefunds = false;
        store.PendingRefundApplies = false;
        store.RefundDelay = TimeSpan.Zero;
        store.CreateDelay = TimeSpan.Zero;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Hook(FakePayment payment, string type) =>
        FakePaymentStore.WebhookBody(payment, FakePaymentOptions.DevOnlyWebhookSecret, factory.Clock.UtcNow, type);

    private async Task<(ActorClient Donor, string Reference, FakePayment Payment)> PaidAsync()
    {
        var (donor, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var payment = factory.Store().Act(PaymentScenario.InvoiceId(checkout), success: true);
        (await factory.WebhookAsync(Hook(payment, "payment_paid"))).Body.Str("outcome").ShouldBe("settled");
        return (donor, checkout.Str("reference"), payment);
    }

    private Task<long> VersionAsync(string reference) =>
        factory.ScalarAsync<long>($"SELECT xmin::text::bigint FROM store.orders WHERE reference = '{reference}'");

    private async Task<(HttpResponseMessage Response, JsonElement Body)> RefundAsync(ActorClient staff, string reference, string amount, string? key = null) =>
        await staff.PostAsync($"/v1/admin/orders/{reference}/refunds",
            new { amount, reason = "طلب المتبرع", version = await VersionAsync(reference) }, HttpJson.Key(key));

    private Task<string> OrderAsync(string reference, string columns) =>
        factory.ScalarAsync<string>($"SELECT concat_ws(':', {columns}) FROM store.orders WHERE reference = '{reference}'");

    private Task<string> RefundsAsync(string reference) =>
        factory.ScalarAsync<string>($"SELECT coalesce(string_agg(r.source || ':' || r.status || ':' || r.amount || ':' || r.requested_by, ',' ORDER BY r.requested_at, r.amount), '') FROM store.refund r JOIN store.orders o ON o.id = r.order_id WHERE o.reference = '{reference}'");

    private Task<long> OutboxAsync(string type, string reference) =>
        factory.ScalarAsync<long>($"SELECT count(*) FROM store.outbox_message WHERE type = '{type}' AND payload->>'reference' = '{reference}'");

    // ── A-1: refunded / voided states never settle as paid ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A1_a_payment_refunded_before_settlement_goes_to_review_as_external_refund_never_paid()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var payment = factory.Store().Act(PaymentScenario.InvoiceId(checkout), success: true);
        payment.Refunded = 1000;                                                  // refunded on the dashboard before any webhook

        (await factory.WebhookAsync(Hook(payment, "payment_refunded"))).Body.Str("outcome").ShouldBe("review");

        (await OrderAsync(reference, "status, review_reason, refunded_amount")).ShouldBe("payment_review:external_refund:10.00");
        (await RefundsAsync(reference)).ShouldBe("gateway:succeeded:10.00:moyasar");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.donation_receipt r JOIN store.orders o ON o.id = r.order_id WHERE o.reference = '{reference}'")).ShouldBe(0);
        (await OutboxAsync("PaymentReviewRequired", reference)).ShouldBe(1);
        (await OutboxAsync("StaffAlert", reference)).ShouldBe(1);

        // Less than the total is left: finance cannot accept it as paid (P-9).
        var finance = await factory.StaffAsync(Roles.Finance);
        var accept = await finance.PostAsync($"/v1/admin/orders/{reference}/review/accept", new { version = await VersionAsync(reference) });
        accept.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        accept.Body.Str("code").ShouldBe(ErrorCodes.PaymentReviewPartial);
    }

    [Fact]
    public async Task A1_a_fully_refunded_payment_before_settlement_is_never_paid()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var payment = factory.Store().Act(PaymentScenario.InvoiceId(checkout), success: true);
        payment.Refunded = payment.Captured;
        payment.Status = "refunded";

        (await factory.WebhookAsync(Hook(payment, "payment_refunded"))).Body.Str("outcome").ShouldBe("review");

        (await OrderAsync(reference, "status, review_reason, refunded_amount")).ShouldBe("refunded:external_refund:32.00");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.donation_receipt r JOIN store.orders o ON o.id = r.order_id WHERE o.reference = '{reference}'")).ShouldBe(0);
        (await OutboxAsync("OrderPaid", reference)).ShouldBe(0);
    }

    [Fact]
    public async Task A1_a_void_without_capture_fails_the_attempt_without_an_alert()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var payment = factory.Store().Act(PaymentScenario.InvoiceId(checkout), success: false);
        payment.Status = "voided";

        (await factory.WebhookAsync(Hook(payment, "payment_voided"))).Body.Str("outcome").ShouldBe("failed");

        (await factory.OrderStatusAsync(reference)).ShouldBe("pending_payment");
        (await OutboxAsync("PaymentReviewRequired", reference)).ShouldBe(0);
        (await OutboxAsync("StaffAlert", reference)).ShouldBe(0);
    }

    [Fact]
    public async Task A1_a_void_after_our_capture_reopens_the_paid_order_for_review()
    {
        var (_, reference, payment) = await PaidAsync();
        payment.Status = "voided";

        (await factory.WebhookAsync(Hook(payment, "payment_voided"))).Body.Str("outcome").ShouldBe("review");

        (await OrderAsync(reference, "status, review_reason")).ShouldBe("payment_review:voided_after_capture");
        (await factory.ScalarAsync<string>($"SELECT p.status || ':' || p.review_reason FROM store.payment p JOIN store.orders o ON o.id = p.order_id WHERE o.reference = '{reference}'"))
            .ShouldBe("review:voided_after_capture");
        (await OutboxAsync("PaymentReviewRequired", reference)).ShouldBe(1);

        (await factory.WebhookAsync(Hook(payment, "payment_voided"))).Body.Str("outcome").ShouldBe("already_settled");   // replay: no second alert
        (await OutboxAsync("PaymentReviewRequired", reference)).ShouldBe(1);
    }

    // ── A-2: a refund made on the gateway's dashboard ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A2_dashboard_refunds_on_a_paid_order_are_imported_partial_then_full_with_a_DRC()
    {
        var (_, reference, payment) = await PaidAsync();

        payment.Refunded = 1200;
        (await factory.WebhookAsync(Hook(payment, "payment_refunded"))).Body.Str("outcome").ShouldBe(PaymentSettlement.RefundImported);
        (await OrderAsync(reference, "status, refunded_amount")).ShouldBe("paid:12.00");
        (await RefundsAsync(reference)).ShouldBe("gateway:succeeded:12.00:moyasar");
        var finance = await factory.StaffAsync(Roles.Finance);
        (await finance.GetAsync($"/v1/admin/orders/{reference}")).Body.GetProperty("refunds").EnumerateArray().ShouldHaveSingleItem().Str("source").ShouldBe("gateway");
        (await OutboxAsync("StaffAlert", reference)).ShouldBe(1);
        (await OutboxAsync("RefundProcessed", reference)).ShouldBe(1);

        (await factory.WebhookAsync(Hook(payment, "payment_refunded"))).Body.Str("outcome").ShouldBe(PaymentSettlement.AlreadySettled);   // same state again
        (await RefundsAsync(reference)).ShouldBe("gateway:succeeded:12.00:moyasar");

        payment.Refunded = payment.Captured;
        payment.Status = "refunded";
        (await factory.WebhookAsync(Hook(payment, "payment_refunded"))).Body.Str("outcome").ShouldBe(PaymentSettlement.RefundImported);
        (await OrderAsync(reference, "status, refunded_amount")).ShouldBe("refunded:32.00");
        (await factory.ScalarAsync<string>($"SELECT string_agg(r.series, ',' ORDER BY r.series) FROM store.donation_receipt r JOIN store.orders o ON o.id = r.order_id WHERE o.reference = '{reference}'"))
            .ShouldBe("DR,DRC");
        (await OutboxAsync("RefundProcessed", reference)).ShouldBe(2);
        (await factory.OrderStatusAsync(reference)).ShouldNotBe("payment_review");
    }

    // ── P-4 / A-6 / P-3: refunds under the lock, pending refunds ─────────────────────────────────────────────────────

    [Fact]
    public async Task P4_two_concurrent_refunds_cannot_exceed_what_was_captured()
    {
        var (_, reference, _) = await PaidAsync();
        var finance = await factory.StaffAsync(Roles.Finance);
        var version = await VersionAsync(reference);
        factory.Store().RefundDelay = TimeSpan.FromMilliseconds(400);

        var both = await Task.WhenAll(
            finance.PostAsync($"/v1/admin/orders/{reference}/refunds", new { amount = "20.00", reason = "a", version }, HttpJson.Key()),
            finance.PostAsync($"/v1/admin/orders/{reference}/refunds", new { amount = "20.00", reason = "b", version }, HttpJson.Key()));

        both.Select(r => (int)r.Response.StatusCode).Order().ShouldBe([200, 422]);
        both.Single(r => r.Response.StatusCode == HttpStatusCode.UnprocessableEntity).Body.Str("code").ShouldBe(ErrorCodes.PaymentRefundExceeds);
        (await OrderAsync(reference, "status, refunded_amount")).ShouldBe("paid:20.00");
    }

    [Fact]
    public async Task P3_a_refund_the_gateway_does_not_confirm_stays_requested_202_and_a_retry_with_the_same_key_reconciles_it()
    {
        var (_, reference, payment) = await PaidAsync();
        var finance = await factory.StaffAsync(Roles.Finance);
        var store = factory.Store();
        store.PendingRefunds = true;
        store.PendingRefundApplies = true;                                       // the gateway did refund; its answer was lost
        var key = $"key-{Guid.NewGuid():N}";

        var first = await RefundAsync(finance, reference, "10.00", key);
        first.Response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        first.Body.Str("refund").ShouldBe("pending");
        first.Body.Str("refundedAmount").ShouldBe("0.00");
        (await RefundsAsync(reference)).ShouldStartWith("store:requested:10.00:");
        payment.Refunded.ShouldBe(1000);

        var other = await RefundAsync(finance, reference, "5.00");               // A-6: one refund at a time
        other.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        other.Body.Str("code").ShouldBe(ErrorCodes.PaymentRefundPending);

        store.PendingRefunds = false;
        var retry = await finance.PostAsync($"/v1/admin/orders/{reference}/refunds",
            new { amount = "10.00", reason = "طلب المتبرع", version = (uint)await VersionAsync(reference) }, HttpJson.Key(key));
        retry.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        retry.Body.Str("refundedAmount").ShouldBe("10.00");
        (await RefundsAsync(reference)).ShouldStartWith("store:succeeded:10.00:");
        payment.Refunded.ShouldBe(1000);                                         // never sent twice
        (await OutboxAsync("RefundProcessed", reference)).ShouldBe(1);
    }

    [Fact]
    public async Task P3_the_refund_job_confirms_a_pending_refund_or_fails_it_after_the_timeout_and_imports_a_late_one()
    {
        var store = factory.Store();
        var finance = await factory.StaffAsync(Roles.Finance);

        var (_, confirmed, _) = await PaidAsync();
        store.PendingRefunds = true;
        store.PendingRefundApplies = true;
        (await RefundAsync(finance, confirmed, "10.00")).Response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var (_, lost, lostPayment) = await PaidAsync();
        store.PendingRefundApplies = false;                                      // never reached the gateway
        (await RefundAsync(finance, lost, "10.00")).Response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        store.PendingRefunds = false;

        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        (await factory.RunAsync<ReconcileRefundsCommand, int>(new ReconcileRefundsCommand())).Value.ShouldBeGreaterThanOrEqualTo(1);
        (await OrderAsync(confirmed, "status, refunded_amount")).ShouldBe("paid:10.00");
        (await RefundsAsync(confirmed)).ShouldStartWith("store:succeeded:10.00:");
        (await RefundsAsync(lost)).ShouldStartWith("store:requested:10.00:");    // not yet past the 30 minutes

        factory.Clock.Advance(TimeSpan.FromMinutes(30));
        await factory.RunAsync<ReconcileRefundsCommand, int>(new ReconcileRefundsCommand());
        (await RefundsAsync(lost)).ShouldStartWith("store:failed:10.00:");
        (await OrderAsync(lost, "status, refunded_amount")).ShouldBe("paid:0.00");

        lostPayment.Refunded = 1000;                                             // it shows up later after all → imported (A-2)
        (await factory.WebhookAsync(Hook(lostPayment, "payment_refunded"))).Body.Str("outcome").ShouldBe(PaymentSettlement.RefundImported);
        (await OrderAsync(lost, "status, refunded_amount")).ShouldBe("paid:10.00");
    }

    // ── P-9 ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task P9_accept_is_refused_when_less_than_the_total_was_captured()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        (await factory.WebhookAsync(factory.Act(PaymentScenario.InvoiceId(checkout), amountOverride: 3100))).Body.Str("outcome").ShouldBe("review");
        var finance = await factory.StaffAsync(Roles.Finance);

        var accept = await finance.PostAsync($"/v1/admin/orders/{reference}/review/accept", new { version = await VersionAsync(reference) });

        accept.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        accept.Body.Str("code").ShouldBe(ErrorCodes.PaymentReviewPartial);
        accept.Body.Str("captured").ShouldBe("31.00");
        accept.Body.Str("total").ShouldBe("32.00");
        (await factory.OrderStatusAsync(reference)).ShouldBe("payment_review");
    }

    // ── A-3: orphan payments ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A3_a_captured_payment_with_no_attempt_of_ours_is_listed_for_finance_and_resolved_once()
    {
        var orphan = factory.Store().OrphanPayment(5000);
        (await factory.WebhookAsync(Hook(orphan, "payment_paid"))).Body.Str("outcome").ShouldBe("orphan");
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.outbox_message WHERE type = 'StaffAlert' AND payload->>'kind' = 'orphan_payment'"))
            .ShouldBeGreaterThanOrEqualTo(1);

        var finance = await factory.StaffAsync(Roles.Finance);
        var openBefore = (await finance.GetAsync("/v1/admin/dashboard")).Body.GetProperty("openOrphanPayments").GetInt32();
        openBefore.ShouldBeGreaterThanOrEqualTo(1);
        var list = await finance.GetAsync("/v1/admin/payments/orphans");
        list.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var row = list.Body.EnumerateArray().Single(r => r.Str("providerPaymentId") == orphan.Id);
        row.Str("amount").ShouldBe("50.00");
        row.Str("currency").ShouldBe("SAR");
        row.Str("providerInvoiceId").ShouldBe(orphan.InvoiceId);
        row.GetProperty("resolvedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        var id = row.Str("id");

        var manager = await factory.StaffAsync(Roles.StoreManager);
        (await manager.GetAsync("/v1/admin/payments/orphans")).Response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await finance.PostAsync($"/v1/admin/payments/orphans/{id}/resolve", new { note = "" })).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var resolved = await finance.PostAsync($"/v1/admin/payments/orphans/{id}/resolve", new { note = "refunded on the dashboard" });
        resolved.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        resolved.Body.Str("resolutionNote").ShouldBe("refunded on the dashboard");
        resolved.Body.Str("resolvedBy").ShouldNotBeNullOrEmpty();

        var again = await finance.PostAsync($"/v1/admin/payments/orphans/{id}/resolve", new { note = "x" });
        again.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        again.Body.Str("code").ShouldBe(ErrorCodes.PaymentOrphanResolved);
        var missing = await finance.PostAsync($"/v1/admin/payments/orphans/{Guid.NewGuid()}/resolve", new { note = "x" });
        missing.Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        missing.Body.Str("code").ShouldBe(ErrorCodes.PaymentOrphanNotFound);

        (await finance.GetAsync("/v1/admin/payments/orphans")).Body.EnumerateArray().Any(r => r.Str("id") == id).ShouldBeFalse();
        (await finance.GetAsync("/v1/admin/dashboard")).Body.GetProperty("openOrphanPayments").GetInt32().ShouldBe(openBefore - 1);
        (await finance.GetAsync("/v1/admin/payments/orphans?resolved=true")).Body.EnumerateArray().Any(r => r.Str("id") == id).ShouldBeTrue();
    }

    // ── A-4 / P-5: one live invoice ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A4_concurrent_pay_requests_leave_exactly_one_live_invoice()
    {
        var (donor, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        factory.Store().CreateDelay = TimeSpan.FromMilliseconds(300);

        var both = await Task.WhenAll(donor.PostAsync($"/v1/orders/{reference}/pay", new { }, HttpJson.Key()), donor.PostAsync($"/v1/orders/{reference}/pay", new { }, HttpJson.Key()));

        both.ShouldAllBe(r => r.Response.StatusCode == HttpStatusCode.OK);
        var live = factory.Store().InvoicesFor(reference).Where(i => i.Status == "initiated").ToList();
        live.ShouldHaveSingleItem();
        (await factory.ScalarAsync<string>($"SELECT p.provider_invoice_id FROM store.payment p JOIN store.orders o ON o.id = p.order_id WHERE o.reference = '{reference}' AND p.status = 'initiated'"))
            .ShouldBe(live[0].Id);
        live[0].ExpiresAt.ShouldBe(await factory.ScalarAsync<DateTime>($"SELECT payment_due_at FROM store.orders WHERE reference = '{reference}'"), TimeSpan.FromSeconds(1));
    }

    // ── P-6 / A-5 / P-8: reconciliation and expiry read every invoice ────────────────────────────────────────────────

    [Fact]
    public async Task P6_reconciliation_settles_a_payment_made_on_a_replaced_attempts_invoice()
    {
        var (donor, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var first = PaymentScenario.InvoiceId(checkout);
        (await donor.PostAsync($"/v1/orders/{reference}/pay", new { }, HttpJson.Key())).Response.StatusCode.ShouldBe(HttpStatusCode.OK);   // attempt 1 cancelled
        factory.Store().Act(first, success: true);                                   // but the donor paid in the old tab; webhook lost

        factory.Clock.Advance(TimeSpan.FromMinutes(6));
        await factory.RunAsync<ReconcilePaymentsCommand, int>(new ReconcilePaymentsCommand());

        (await factory.OrderStatusAsync(reference)).ShouldBe("paid");
    }

    [Fact]
    public async Task A5_reconciliation_finds_a_late_capture_on_an_expired_order()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        factory.Clock.Advance(TimeSpan.FromMinutes(61));
        await factory.RunAsync<ExpireUnpaidOrdersCommand, int>(new ExpireUnpaidOrdersCommand());
        (await factory.OrderStatusAsync(reference)).ShouldBe("expired");
        factory.Store().Act(PaymentScenario.InvoiceId(checkout), success: true);     // late, webhook lost

        factory.Clock.Advance(TimeSpan.FromMinutes(6));
        await factory.RunAsync<ReconcilePaymentsCommand, int>(new ReconcilePaymentsCommand());

        (await OrderAsync(reference, "status, review_reason")).ShouldBe("payment_review:late_capture");
    }

    [Fact]
    public async Task P6_expiry_re_reads_the_invoice_of_a_failed_attempt_first()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var invoice = PaymentScenario.InvoiceId(checkout);
        (await factory.WebhookAsync(factory.Act(invoice, success: false))).Body.Str("outcome").ShouldBe("failed");
        factory.Store().Act(invoice, success: true);                                 // second try on the same page; webhook lost

        factory.Clock.Advance(TimeSpan.FromMinutes(61));
        await factory.RunAsync<ExpireUnpaidOrdersCommand, int>(new ExpireUnpaidOrdersCommand());

        (await factory.OrderStatusAsync(reference)).ShouldBe("paid");
    }

    [Fact]
    public async Task P6_pay_after_a_failed_attempt_cancels_its_invoice()
    {
        var (donor, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var invoice = PaymentScenario.InvoiceId(checkout);
        await factory.WebhookAsync(factory.Act(invoice, success: false));

        (await donor.PostAsync($"/v1/orders/{reference}/pay", new { }, HttpJson.Key())).Response.StatusCode.ShouldBe(HttpStatusCode.OK);

        factory.Store().Invoice(invoice)!.Status.ShouldBe("canceled");
        factory.Store().InvoicesFor(reference).Count(i => i.Status == "initiated").ShouldBe(1);
    }

    [Fact]
    public async Task P8_expiry_skips_an_order_whose_gateway_re_check_failed()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        factory.Clock.Advance(TimeSpan.FromMinutes(61));
        factory.Store().FailReads = true;

        await factory.RunAsync<ExpireUnpaidOrdersCommand, int>(new ExpireUnpaidOrdersCommand());
        (await factory.OrderStatusAsync(reference)).ShouldBe("pending_payment");

        factory.Store().FailReads = false;
        await factory.RunAsync<ExpireUnpaidOrdersCommand, int>(new ExpireUnpaidOrdersCommand());
        (await factory.OrderStatusAsync(reference)).ShouldBe("expired");
    }

    // ── S-2 ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task S2_a_capture_without_a_currency_is_a_mismatch()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var payment = factory.Store().Act(PaymentScenario.InvoiceId(checkout), success: true);
        payment.Currency = null;

        (await factory.WebhookAsync(Hook(payment, "payment_paid"))).Body.Str("outcome").ShouldBe("review");
        (await OrderAsync(reference, "status, review_reason")).ShouldBe("payment_review:amount_mismatch");
    }

    // ── S-3 / S-4 / S-5: the webhook inbox ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task S3_a_rejected_webhook_keeps_fixed_size_metadata_only()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var body = factory.Act(PaymentScenario.InvoiceId(checkout)).Replace(FakePaymentOptions.DevOnlyWebhookSecret, "wrong-secret-0123456789", StringComparison.Ordinal);

        (await factory.WebhookAsync(body)).Response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body)));
        (await factory.ScalarAsync<string>($"SELECT payload_json::text FROM store.payment_event WHERE payload_json->>'sha256' = '{sha}'"))
            .ShouldBe($$"""{"type": null, "length": {{System.Text.Encoding.UTF8.GetByteCount(body)}}, "sha256": "{{sha}}"}""");
    }

    [Fact]
    public async Task S4_the_re_fetch_gives_up_after_two_short_tries_and_answers_503()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var body = factory.Act(PaymentScenario.InvoiceId(checkout));
        var store = factory.Store();
        store.ReadDelay = TimeSpan.FromSeconds(30);
        store.DelayNextReads = 2;
        try
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            (await factory.WebhookAsync(body)).Response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20));
        }
        finally
        {
            store.ReadDelay = TimeSpan.Zero;
            store.DelayNextReads = 0;
        }

        (await factory.WebhookAsync(body)).Body.Str("outcome").ShouldBe("settled");    // the provider's retry
        (await factory.OrderStatusAsync(reference)).ShouldBe("paid");
    }

    [Fact]
    public async Task S5_an_event_claimed_by_another_delivery_is_left_to_it_until_the_claim_is_stale()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var body = factory.Act(PaymentScenario.InvoiceId(checkout));
        var eventId = JsonDocument.Parse(body).RootElement.GetProperty("id").GetString();
        factory.Store().FailReads = true;
        (await factory.WebhookAsync(body)).Response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);   // recorded, claim released
        factory.Store().FailReads = false;

        var now = factory.Clock.UtcNow;
        await factory.ExecuteSqlAsync($"UPDATE store.payment_event SET claimed_at = '{now.AddSeconds(-30):O}' WHERE provider_event_id = '{eventId}'");
        (await factory.WebhookAsync(body)).Body.Str("outcome").ShouldBe("duplicate");     // another delivery is on it
        (await factory.OrderStatusAsync(reference)).ShouldBe("pending_payment");

        await factory.ExecuteSqlAsync($"UPDATE store.payment_event SET claimed_at = '{now.AddMinutes(-3):O}' WHERE provider_event_id = '{eventId}'");
        (await factory.WebhookAsync(body)).Body.Str("outcome").ShouldBe("settled");       // that one died: taken over
        (await OutboxAsync("OrderPaid", reference)).ShouldBe(1);
    }

    [Fact]
    public async Task S5_parallel_deliveries_of_one_event_settle_it_once()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var body = factory.Act(PaymentScenario.InvoiceId(checkout));

        var all = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => factory.WebhookAsync(body)));

        all.Count(r => r.Body.Str("outcome") == "settled").ShouldBe(1);
        (await OutboxAsync("OrderPaid", reference)).ShouldBe(1);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.payment_event WHERE provider_event_id = '{JsonDocument.Parse(body).RootElement.GetProperty("id").GetString()}'")).ShouldBe(1);
    }
}
