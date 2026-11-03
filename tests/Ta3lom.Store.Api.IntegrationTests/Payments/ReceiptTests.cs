using System.Net;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Payments;

/// Plan §4 (F3a): DR receipts — download (owner / status token), private storage, the donor messages after payment,
/// gapless numbering under concurrency, and D-14 anonymisation.
public sealed class ReceiptTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private async Task<(ActorClient Donor, string Reference, string Token)> PaidAsync()
    {
        var (donor, checkout) = await PaymentScenario.CheckoutAsync(factory);
        (await factory.WebhookAsync(factory.Act(PaymentScenario.InvoiceId(checkout)))).Body.Str("outcome").ShouldBe("settled");
        return (donor, checkout.Str("reference"), checkout.Str("statusToken"));
    }

    [Fact]
    public async Task The_status_token_holder_and_the_donor_download_the_receipt_as_a_private_attachment()
    {
        var (donor, reference, token) = await PaidAsync();
        var anonymous = factory.Anonymous();

        var byToken = await anonymous.GetAsync($"/v1/orders/{reference}/receipt?t={Uri.EscapeDataString(token)}", TestContext.Current.CancellationToken);
        byToken.StatusCode.ShouldBe(HttpStatusCode.OK);
        byToken.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        byToken.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        byToken.Content.Headers.ContentDisposition.FileName!.Trim('"').ShouldMatch(@"^DR-\d{4}-\d{6}\.pdf$");
        byToken.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var pdf = await byToken.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        ReceiptPdfTests.Extract(pdf).ShouldContain(reference);

        var mine = await donor.Http.GetAsync($"/v1/me/donations/{reference}/receipt", TestContext.Current.CancellationToken);
        mine.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await mine.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBe(pdf);   // rendered once, stored, read back

        (await factory.ScalarAsync<string>($"SELECT r.pdf_storage_key FROM store.donation_receipt r JOIN store.orders o ON o.id = r.order_id WHERE o.reference = '{reference}'"))
            .ShouldStartWith("receipts/");
        (await anonymous.GetAsync($"/v1/orders/{reference}/receipt?t=wrong", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var other = await factory.CustomerAsync();
        (await other.Http.GetAsync($"/v1/me/donations/{reference}/receipt", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/v1/me/donations/{reference}/receipt", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_unpaid_order_has_no_receipt_yet()
    {
        var (donor, checkout) = await PaymentScenario.CheckoutAsync(factory);
        var r = await donor.GetAsync($"/v1/me/donations/{checkout.Str("reference")}/receipt");
        r.Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        r.Body.Str("code").ShouldBe(ErrorCodes.ReceiptNotAvailable);
        var drc = await donor.GetAsync($"/v1/me/donations/{checkout.Str("reference")}/receipt?kind=drc");
        drc.Body.Str("code").ShouldBe(ErrorCodes.ReceiptNotAvailable);
    }

    [Fact]
    public async Task Payment_sends_one_SMS_and_one_e_mail_with_the_receipt_and_renders_the_PDF_once()
    {
        var (donor, reference, _) = await PaidAsync();
        await factory.DrainOutboxAsync();
        await factory.DrainOutboxAsync();

        var sms = factory.SmsTo(donor.Mobile!).Select(m => m.Body).Where(m => m.Contains(reference, StringComparison.Ordinal)).ToList();
        sms.ShouldHaveSingleItem().ShouldContain($"/orders/{reference}?t=");
        var mail = (await factory.MailAsync()).Where(m => m.To == "donor@example.com" && m.Body.Contains(reference, StringComparison.Ordinal)).ToList();
        mail.ShouldHaveSingleItem().Attachments.ShouldHaveSingleItem().ShouldMatch(@"^DR-\d{4}-\d{6}\.pdf$");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.donation_receipt r JOIN store.orders o ON o.id = r.order_id WHERE o.reference = '{reference}' AND r.pdf_storage_key IS NOT NULL"))
            .ShouldBe(1);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.outbox_message WHERE dead_lettered")).ShouldBe(0);
    }

    [Fact]
    public async Task Receipt_numbers_have_no_gap_under_ten_parallel_settlements()
    {
        var bodies = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            var (_, checkout) = await PaymentScenario.CheckoutAsync(factory);
            bodies.Add(factory.Act(PaymentScenario.InvoiceId(checkout)));
        }

        var results = await Task.WhenAll(bodies.Select(b => factory.WebhookAsync(b)));

        results.ShouldAllBe(r => r.Response.StatusCode == HttpStatusCode.OK);
        var year = factory.Clock.UtcNow.ToOffset(TimeSpan.FromHours(3)).Year;
        (await factory.ScalarAsync<bool>($"SELECT count(*) = max(number) AND min(number) = 1 AND count(DISTINCT number) = count(*) FROM store.donation_receipt WHERE series = 'DR' AND year = {year}"))
            .ShouldBeTrue();
        (await factory.ScalarAsync<int>($"SELECT next_number - 1 FROM store.document_counter WHERE series = 'DR' AND year = {year}"))
            .ShouldBe(await factory.ScalarAsync<int>($"SELECT count(*)::int FROM store.donation_receipt WHERE series = 'DR' AND year = {year}"));
    }

    [Fact]
    [Rule("D-14")]
    public async Task Deleting_the_donor_anonymises_the_receipt_name_and_drops_the_stored_PDF()
    {
        var (donor, reference, _) = await PaidAsync();
        (await donor.Http.GetAsync($"/v1/me/donations/{reference}/receipt", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await donor.SendAsync(HttpMethod.Delete, "/v1/me")).Response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await factory.ScalarAsync<string>($"SELECT r.donor_name_snapshot || '|' || coalesce(r.pdf_storage_key, 'none') || '|' || r.amount FROM store.donation_receipt r JOIN store.orders o ON o.id = r.order_id WHERE o.reference = '{reference}'"))
            .ShouldBe("—|none|32.00");
    }
}
