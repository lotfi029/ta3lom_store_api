using System.Net;
using System.Text;
using Ta3lom.Store.Api.IntegrationTests.Donations;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Features.Payments;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Payments;

/// Plan §4 (F3c): the admin order detail with payments/events/refunds/receipts, receipt resend, dashboard paid totals,
/// and the finance CSV.
public sealed class FinanceViewTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private async Task<(ActorClient Donor, string Reference)> PaidAsync()
    {
        var (donor, checkout) = await PaymentScenario.CheckoutAsync(factory);
        await factory.WebhookAsync(factory.Act(PaymentScenario.InvoiceId(checkout), success: false));
        var pay = await donor.PostAsync($"/v1/orders/{checkout.Str("reference")}/pay", new { }, HttpJson.Key());
        (await factory.WebhookAsync(factory.Act(PaymentScenario.InvoiceId(pay.Body)))).Body.Str("outcome").ShouldBe("settled");
        return (donor, checkout.Str("reference"));
    }

    [Fact]
    public async Task The_admin_order_detail_shows_attempts_events_refunds_and_receipts()
    {
        var (_, reference) = await PaidAsync();
        var finance = await factory.StaffAsync(Roles.Finance);
        var detail = await finance.GetAsync($"/v1/admin/orders/{reference}");
        var version = detail.Body.GetProperty("version").GetInt64();
        await finance.PostAsync($"/v1/admin/orders/{reference}/refunds", new { amount = "2.00", reason = "تجربة", version }, HttpJson.Key());

        var d = (await finance.GetAsync($"/v1/admin/orders/{reference}")).Body;

        d.Str("status").ShouldBe("paid");
        d.Str("refundedAmount").ShouldBe("2.00");
        d.Str("refundable").ShouldBe("30.00");
        d.GetProperty("paidAt").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.String);
        d.GetProperty("payments").EnumerateArray().Select(p => $"{p.GetProperty("attemptNo").GetInt32()}:{p.Str("status")}").ShouldBe(["1:failed", "2:captured"]);
        d.GetProperty("payments")[1].Str("method").ShouldBe("mada");
        d.GetProperty("events").EnumerateArray().Select(e => e.Str("outcome")).ShouldBe(["failed", "settled"]);
        d.GetProperty("events")[0].TryGetProperty("payloadJson", out _).ShouldBeFalse();
        d.GetProperty("refunds").EnumerateArray().ShouldHaveSingleItem().Str("status").ShouldBe("succeeded");
        d.GetProperty("receipts").EnumerateArray().ShouldHaveSingleItem().Str("number").ShouldMatch(@"^DR-\d{4}-\d{6}$");
    }

    [Fact]
    public async Task Resend_sends_the_paid_messages_again_once_per_key()
    {
        var (donor, reference) = await PaidAsync();
        await factory.DrainOutboxAsync();
        var finance = await factory.StaffAsync(Roles.Finance);
        var before = factory.SmsTo(donor.Mobile!).Count(m => m.Body.Contains(reference, StringComparison.Ordinal));
        var key = HttpJson.Key();

        (await finance.PostAsync($"/v1/admin/orders/{reference}/receipt/resend", new { }, key)).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await finance.PostAsync($"/v1/admin/orders/{reference}/receipt/resend", new { }, key)).Response.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        await factory.DrainOutboxAsync();

        factory.SmsTo(donor.Mobile!).Count(m => m.Body.Contains(reference, StringComparison.Ordinal)).ShouldBe(before + 1);
        var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var unpaid = await finance.PostAsync($"/v1/admin/orders/{checkout.Str("reference")}/receipt/resend", new { }, HttpJson.Key());
        unpaid.Body.Str("code").ShouldBe(ErrorCodes.ReceiptNotAvailable);
    }

    [Fact]
    public async Task The_dashboard_shows_paid_totals_and_top_projects_by_paid_amount()
    {
        await PaidAsync();
        var viewer = await factory.StaffAsync(Roles.Viewer);
        var d = (await viewer.GetAsync("/v1/admin/dashboard")).Body;
        d.GetProperty("paidToday").GetProperty("count").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        decimal.Parse(d.GetProperty("paidThisMonth").Str("amount"), System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(32m);
        d.GetProperty("topProjectsByPaidAmount").GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);
        d.GetProperty("inReviewOrders").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Number);
    }

    [Fact]
    public async Task The_donations_CSV_has_a_BOM_masked_mobiles_and_Riyadh_dates()
    {
        var (donor, reference) = await PaidAsync();
        var finance = await factory.StaffAsync(Roles.Finance);
        var today = factory.Clock.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var csv = await finance.Http.GetAsync($"/v1/admin/reports/donations.csv?from={today}&to={today}", TestContext.Current.CancellationToken);

        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        csv.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var bytes = await csv.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble());
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var row = text.Split("\r\n").Single(l => l.StartsWith(reference, StringComparison.Ordinal));
        row.ShouldContain("عبدالله");
        row.ShouldNotContain(donor.Mobile![^7..]);
        row.ShouldContain(today);
        row.ShouldContain("32.00");
        row.ShouldContain("mada");
        text.Split("\r\n")[0].ShouldStartWith("reference,paid_at_riyadh,status,donor_name,donor_mobile");

        var bad = await finance.GetAsync("/v1/admin/reports/donations.csv?from=2026-12-01&to=2026-01-01");
        bad.Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await (await factory.StaffAsync(Roles.StoreManager)).Http.GetAsync($"/v1/admin/reports/donations.csv", TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+966", "'+966")]
    [InlineData("-1", "'-1")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("عبدالله", "عبدالله")]
    public void CSV_cells_are_escaped_and_injection_safe(string value, string expected) =>
        DonationsCsvQueryHandler.Cell(value).ShouldBe(expected);
}
