using System.Net;
using Ta3lom.Store.Api.IntegrationTests.Donations;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Payments;

/// F-D5 / plan §4 (F3b): finance refunds by amount (partial keeps `paid`, full → `refunded` + DRC), payment review
/// accept/refund, idempotency, bounds, gateway refusal, roles.
public sealed class RefundTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private async Task<(ActorClient Donor, string Reference)> PaidAsync()
    {
        var (donor, checkout) = await PaymentScenario.CheckoutAsync(factory);
        (await factory.WebhookAsync(factory.Act(PaymentScenario.InvoiceId(checkout)))).Body.Str("outcome").ShouldBe("settled");
        return (donor, checkout.Str("reference"));
    }

    private Task<long> VersionAsync(string reference) =>
        factory.ScalarAsync<long>($"SELECT xmin::text::bigint FROM store.orders WHERE reference = '{reference}'");

    private async Task<(HttpResponseMessage Response, System.Text.Json.JsonElement Body)> RefundAsync(ActorClient staff, string reference, string amount,
        string? key = null, long? version = null) =>
        await staff.PostAsync($"/v1/admin/orders/{reference}/refunds",
            new { amount, reason = "طلب المتبرع", version = version ?? await VersionAsync(reference) }, HttpJson.Key(key));

    [Fact]
    public async Task A_partial_then_a_full_refund_ends_refunded_with_a_DRC_voiding_the_DR()
    {
        var (donor, reference) = await PaidAsync();
        var finance = await factory.StaffAsync(Roles.Finance);

        var partial = await RefundAsync(finance, reference, "12.00");
        partial.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        partial.Body.Str("status").ShouldBe("paid");
        partial.Body.Str("refundedAmount").ShouldBe("12.00");

        var rest = await RefundAsync(finance, reference, "20.00");
        rest.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        rest.Body.Str("status").ShouldBe("refunded");
        rest.Body.Str("refundedAmount").ShouldBe("32.00");

        (await factory.ScalarAsync<string>($"SELECT string_agg(r.series || ':' || coalesce(r.voided_by_receipt_id::text, '-') , ',' ORDER BY r.series) FROM store.donation_receipt r JOIN store.orders o ON o.id = r.order_id WHERE o.reference = '{reference}'"))
            .ShouldMatch("^DR:[0-9a-f-]{36},DRC:-$");
        (await factory.ScalarAsync<string>($"SELECT p.status || ':' || p.refunded_amount FROM store.payment p JOIN store.orders o ON o.id = p.order_id WHERE o.reference = '{reference}'"))
            .ShouldBe("refunded:32.00");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE action = 'order.refunded'")).ShouldBeGreaterThanOrEqualTo(2);

        await factory.DrainOutboxAsync();
        await factory.DrainOutboxAsync();
        factory.SmsTo(donor.Mobile!).Count(m => m.Body.Contains(reference, StringComparison.Ordinal) && m.Body.Contains("استرداد", StringComparison.Ordinal)).ShouldBe(2);
        (await factory.MailAsync()).Where(m => m.Body.Contains(reference, StringComparison.Ordinal) && m.Attachments.Any(a => a.StartsWith("DRC-", StringComparison.Ordinal)))
            .ShouldHaveSingleItem();
        var drc = await donor.Http.GetAsync($"/v1/me/donations/{reference}/receipt?kind=drc", TestContext.Current.CancellationToken);
        drc.StatusCode.ShouldBe(HttpStatusCode.OK);
        ReceiptPdfTests.Extract(await drc.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldContain("DRC-");
    }

    [Fact]
    public async Task Refunds_are_bounded_idempotent_and_versioned()
    {
        var (_, reference) = await PaidAsync();
        var finance = await factory.StaffAsync(Roles.Admin);

        var tooMuch = await RefundAsync(finance, reference, "32.01");
        tooMuch.Response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        tooMuch.Body.Str("code").ShouldBe(ErrorCodes.PaymentRefundExceeds);
        tooMuch.Body.Str("refundable").ShouldBe("32.00");

        var stale = await RefundAsync(finance, reference, "1.00", version: 1);
        stale.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        stale.Body.Str("code").ShouldBe(ErrorCodes.ConcurrencyConflict);

        var key = $"key-{Guid.NewGuid():N}";
        var version = await VersionAsync(reference);
        var first = await RefundAsync(finance, reference, "5.00", key, version);
        var replay = await RefundAsync(finance, reference, "5.00", key, version);      // same key, same body
        var reused = await RefundAsync(finance, reference, "6.00", key, version);      // same key, another body
        reused.Response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        reused.Body.Str("code").ShouldBe(ErrorCodes.IdempotencyKeyReused);
        first.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.Response.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.refund r JOIN store.orders o ON o.id = r.order_id WHERE o.reference = '{reference}'")).ShouldBe(1);

        var noKey = await finance.PostAsync($"/v1/admin/orders/{reference}/refunds", new { amount = "1.00", reason = "x", version = await VersionAsync(reference) });
        noKey.Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        noKey.Body.Str("code").ShouldBe(ErrorCodes.IdempotencyKeyRequired);
    }

    [Fact]
    public async Task A_gateway_refusal_changes_nothing_and_answers_502()
    {
        var (_, reference) = await PaidAsync();
        var finance = await factory.StaffAsync(Roles.Finance);
        factory.Store().FailRefunds = true;
        try
        {
            var refused = await RefundAsync(finance, reference, "10.00");
            refused.Response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
            refused.Body.Str("code").ShouldBe(ErrorCodes.PaymentRefundFailed);
        }
        finally
        {
            factory.Store().FailRefunds = false;
        }

        (await factory.OrderStatusAsync(reference)).ShouldBe("paid");
        (await factory.ScalarAsync<string>($"SELECT o.refunded_amount || ':' || r.status FROM store.orders o JOIN store.refund r ON r.order_id = o.id WHERE o.reference = '{reference}'"))
            .ShouldBe("0.00:failed");
    }

    [Fact]
    public async Task Only_finance_and_admin_refund_and_a_pending_order_cannot_be_refunded()
    {
        var (_, reference) = await PaidAsync();
        var manager = await factory.StaffAsync(Roles.StoreManager);
        (await RefundAsync(manager, reference, "1.00")).Response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var finance = await factory.StaffAsync(Roles.Finance);
        var pending = await RefundAsync(finance, checkout.Str("reference"), "1.00");
        pending.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        pending.Body.Str("code").ShouldBe(ErrorCodes.PaymentRefundNotAllowed);
    }

    [Fact]
    public async Task Review_accept_settles_the_order_in_full_with_a_DR()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        await factory.WebhookAsync(factory.Act(PaymentScenario.InvoiceId(checkout), amountOverride: 3300));
        var finance = await factory.StaffAsync(Roles.Finance);

        var normalRefund = await RefundAsync(finance, reference, "1.00");
        normalRefund.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        normalRefund.Body.Str("code").ShouldBe(ErrorCodes.PaymentReviewRequired);

        var accept = await finance.PostAsync($"/v1/admin/orders/{reference}/review/accept", new { version = await VersionAsync(reference) });
        accept.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        accept.Body.Str("status").ShouldBe("paid");
        accept.Body.Str("receiptNumber").ShouldMatch(@"^DR-\d{4}-\d{6}$");
        (await factory.ScalarAsync<string>($"SELECT p.status FROM store.payment p JOIN store.orders o ON o.id = p.order_id WHERE o.reference = '{reference}'")).ShouldBe("captured");
        var again = await finance.PostAsync($"/v1/admin/orders/{reference}/review/accept", new { version = await VersionAsync(reference) });
        again.Response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        again.Body.Str("code").ShouldBe(ErrorCodes.PaymentNotInReview);
    }

    [Fact]
    public async Task Review_refund_returns_what_was_captured_and_the_order_ends_refunded()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        await factory.WebhookAsync(factory.Act(PaymentScenario.InvoiceId(checkout), amountOverride: 3300));
        var finance = await factory.StaffAsync(Roles.Finance);

        var refund = await finance.PostAsync($"/v1/admin/orders/{reference}/review/refund", new { version = await VersionAsync(reference), reason = "مبلغ خاطئ" }, HttpJson.Key());

        refund.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        refund.Body.Str("status").ShouldBe("refunded");
        refund.Body.Str("refundedAmount").ShouldBe("33.00");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.donation_receipt r JOIN store.orders o ON o.id = r.order_id WHERE o.reference = '{reference}'")).ShouldBe(0);
    }

    [Fact]
    public async Task A_double_payment_is_refunded_without_touching_the_paid_order()
    {
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var reference = checkout.Str("reference");
        var invoice = PaymentScenario.InvoiceId(checkout);
        await factory.WebhookAsync(factory.Act(invoice));
        (await factory.WebhookAsync(factory.Act(invoice))).Body.Str("outcome").ShouldBe("review");
        var finance = await factory.StaffAsync(Roles.Finance);

        var refund = await finance.PostAsync($"/v1/admin/orders/{reference}/review/refund", new { version = await VersionAsync(reference), reason = "دفع مكرر" }, HttpJson.Key());

        refund.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        refund.Body.Str("status").ShouldBe("paid");
        refund.Body.Str("refundedAmount").ShouldBe("0.00");
        (await factory.ScalarAsync<string>($"SELECT string_agg(p.attempt_no || ':' || p.status || ':' || p.refunded_amount, ',' ORDER BY p.attempt_no) FROM store.payment p JOIN store.orders o ON o.id = p.order_id WHERE o.reference = '{reference}'"))
            .ShouldBe("1:captured:0.00,2:refunded:32.00");
    }

    /// Review 04 S-13: an Idempotency-Key is the staff member's own — another staff member's request with the same key is
    /// a different refund, never a replay of the first.
    [Fact]
    public async Task S13_the_same_key_from_two_staff_members_is_two_refunds()
    {
        var (_, reference) = await PaidAsync();
        var first = await factory.StaffAsync(Roles.Finance);
        var second = await factory.StaffAsync(Roles.Finance);
        var key = $"key-{Guid.NewGuid():N}";

        (await RefundAsync(first, reference, "1.00", key)).Body.Str("refundedAmount").ShouldBe("1.00");
        (await RefundAsync(second, reference, "2.00", key)).Body.Str("refundedAmount").ShouldBe("3.00");

        (await factory.ScalarAsync<long>($"SELECT count(DISTINCT r.requested_by) FROM store.refund r JOIN store.orders o ON o.id = r.order_id WHERE o.reference = '{reference}' AND r.status = 'succeeded'"))
            .ShouldBe(2);
    }
}
