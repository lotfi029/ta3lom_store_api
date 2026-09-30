using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Features.Allocation.Commands;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.Scenarios;

/// T-5 — fulfilment, documents and giving rules (plan 14 S-17, S-19, S-20, S-21, S-24, S-25, S-26: RUL-22, 40 … 42, 49, 51, 53,
/// 54, 59).
public sealed class FulfilmentAndGivingRulesTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private ActorClient? _admin;

    private async Task<ActorClient> AdminAsync() => _admin ??= await factory.StaffAsync(Roles.Admin, Roles.StoreManager, Roles.Fulfilment);

    private static string Q(Guid id) => $"'{id}'";

    [Fact]
    [Rule("RUL-40")]
    public async Task The_order_is_fulfilled_when_every_line_is_terminal_print_only_when_delivered()
    {
        var customer = await factory.CustomerAsync();
        var order = await factory.PaidOrderAsync(customer,
            Shop.Buy(await factory.ProductIdAsync("digital-teacher")),
            Shop.Buy(await factory.ProductIdAsync("science-stars-book"), 1, await factory.FormatIdAsync("science-stars-book", "print")),
            Shop.Donate(await factory.ProductIdAsync("school-bag")));

        var statuses = await factory.ScalarAsync<string>($"SELECT string_agg(kind || ':' || fulfilment_status, ',' ORDER BY line_no) FROM store.order_line WHERE order_id = {Q(order.OrderId)}");
        statuses.ShouldBe("buy:enrolled,buy:preparing,donate:received");
        (await factory.ScalarAsync<string>($"SELECT status FROM store.orders WHERE id = {Q(order.OrderId)}")).ShouldBe("paid");

        var admin = await AdminAsync();
        var shipment = await factory.ScalarAsync<Guid>($"SELECT id FROM store.shipment WHERE order_id = {Q(order.OrderId)}");
        (await admin.PostAsync($"/v1/admin/shipments/{shipment}/ship", new { carrier = "SMSA", trackingNo = "T5-1" })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await factory.ScalarAsync<string>($"SELECT status FROM store.orders WHERE id = {Q(order.OrderId)}")).ShouldBe("paid");     // shipped is not terminal
        (await admin.PostAsync($"/v1/admin/shipments/{shipment}/deliver", new { deliveredOn = factory.Clock.RiyadhToday })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await factory.ScalarAsync<string>($"SELECT status FROM store.orders WHERE id = {Q(order.OrderId)}")).ShouldBe("fulfilled");
    }

    [Fact]
    [Rule("RUL-41")]
    public async Task Downloads_are_signed_per_user_limited_to_five_expire_after_ten_minutes_and_staff_can_reset()
    {
        var admin = await AdminAsync();
        var book = await factory.ProductIdAsync("letters-tales");
        var digital = await factory.FormatIdAsync("letters-tales", "digital");
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent("book_file"), "kind");
            var file = new ByteArrayContent(ScenarioFactory.MinimalPdf());
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(file, "file", "letters.pdf");
            var uploaded = await admin.Http.PostAsync("/v1/admin/media", form, TestContext.Current.CancellationToken);
            uploaded.StatusCode.ShouldBe(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var asset = JsonDocument.Parse(await uploaded.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.GetProperty("id").GetGuid();
            await factory.ExecuteSqlAsync($"UPDATE store.book_format SET file_asset_id = {Q(asset)} WHERE id = {Q(digital)}");
        }

        var buyer = await factory.CustomerAsync();
        await factory.PaidOrderAsync(buyer, Shop.Buy(book, 1, digital));
        var grant = (await buyer.GetAsync("/v1/me/books")).Body.GetProperty("downloads")[0].GetProperty("grantId").GetGuid();

        (await (await factory.CustomerAsync()).PostAsync($"/v1/me/downloads/{grant}/link")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);   // per user
        for (var i = 0; i < 5; i++)
        {
            var (_, link) = await buyer.PostAsync($"/v1/me/downloads/{grant}/link");
            var file = await factory.Anonymous().GetAsync(new Uri(link.GetProperty("url").GetString()!).PathAndQuery, TestContext.Current.CancellationToken);
            file.StatusCode.ShouldBe(HttpStatusCode.OK);
            file.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        }

        (await buyer.PostAsync($"/v1/me/downloads/{grant}/link")).Body.Code().ShouldBe("download.limit_reached");
        var (reset, resetBody) = await admin.PostAsync($"/v1/admin/download-grants/{grant}/reset", new { reason = "طلب العميل" });
        reset.IsSuccessStatusCode.ShouldBeTrue($"{(int)reset.StatusCode} {resetBody}");
        var (_, fresh) = await buyer.PostAsync($"/v1/me/downloads/{grant}/link");
        fresh.GetProperty("remaining").GetInt32().ShouldBe(5);

        var start = factory.Clock.UtcNow;
        try
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(11));
            var expired = await factory.Anonymous().GetAsync(new Uri(fresh.GetProperty("url").GetString()!).PathAndQuery, TestContext.Current.CancellationToken);
            expired.StatusCode.ShouldBe(HttpStatusCode.Gone);
        }
        finally
        {
            factory.Clock.Set(start);
        }
    }

    [Fact]
    [Rule("RUL-42")]
    public async Task Course_access_is_lifetime_unless_access_days_and_is_granted_through_the_LMS()
    {
        var lifetime = await factory.ProductIdAsync("smart-study");
        var limited = await factory.ProductIdAsync("family-remote");
        await factory.ExecuteSqlAsync($"UPDATE store.course_detail SET access_days = 30 WHERE product_id = {Q(limited)}");
        var customer = await factory.CustomerAsync();

        var order = await customer.OrderAsync(Shop.Buy(lifetime), Shop.Buy(limited));
        await factory.SettleAsync(order.Reference);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.enrolment WHERE customer_id = {Q(customer.Id)} AND status = 'pending_sync'")).ShouldBe(2);
        await factory.DrainOutboxAsync();                                                                   // LmsEnrol

        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.enrolment WHERE customer_id = {Q(customer.Id)} AND status = 'active'")).ShouldBe(2);
        (await factory.ScalarAsync<bool>($"SELECT expires_at IS NULL FROM store.enrolment WHERE customer_id = {Q(customer.Id)} AND product_id = {Q(lifetime)}")).ShouldBeTrue();
        (await factory.ScalarAsync<int>($"SELECT (expires_at::date - granted_at::date) FROM store.enrolment WHERE customer_id = {Q(customer.Id)} AND product_id = {Q(limited)}")).ShouldBe(30);
    }

    [Fact]
    [Rule("RUL-22")]
    public async Task The_card_SMS_goes_only_after_payment_once_and_the_card_shows_no_amount()
    {
        var recipient = ScenarioFactory.NewMobile();
        var customer = await factory.CustomerAsync();
        var order = await customer.OrderAsync(new
        {
            productId = await factory.ProductIdAsync("water-for-schools"),
            kind = "donate",
            mode = "units",
            quantity = 1,
            intent = "sadaqa",
            dedication = new { toName = "أم خالد", toMobile = recipient, fromName = "نورة", templateKey = "hasanat" },
        });
        await factory.DrainOutboxAsync();
        factory.SmsTo(recipient).ShouldBeEmpty();
        var token = await factory.ScalarAsync<string>($"SELECT d.card_token FROM store.dedication d JOIN store.order_line l ON l.id = d.order_line_id WHERE l.order_id = {Q(order.OrderId)}");
        (await factory.Anonymous().GetAsync($"/v1/cards/{token}", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await factory.SettleAsync(order.Reference);
        await factory.DrainOutboxAsync();
        await factory.DrainOutboxAsync();

        factory.SmsTo(recipient).Count.ShouldBe(1);
        factory.SmsTo(recipient)[0].Body.ShouldContain(token);
        var (card, body) = await new ActorClient(factory.Anonymous(), Guid.Empty, null, null).GetAsync($"/v1/cards/{token}");
        card.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("toName").GetString().ShouldBe("أم خالد");
        body.EnumerateObject().Select(p => p.Name).ShouldNotContain(n => n.Contains("amount", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Rule("RUL-53")]
    public async Task Cause_units_record_quantity_times_the_unit_amount()
    {
        var order = await factory.PaidOrderAsync(await factory.CustomerAsync(), Shop.Donate(await factory.ProductIdAsync("school-bag"), 3));   // 180.00 a bag

        (await factory.ScalarAsync<decimal>($"SELECT amount FROM store.order_line WHERE order_id = {Q(order.OrderId)}")).ShouldBe(540.00m);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.sponsored_unit su JOIN store.order_line l ON l.id = su.order_line_id WHERE l.order_id = {Q(order.OrderId)}")).ShouldBe(0);
    }

    [Fact]
    [Rule("RUL-54")]
    public async Task Updates_notify_each_donor_once_and_private_ones_stay_off_the_public_page()
    {
        var cause = await factory.ProductIdAsync("tablet-for-student");
        var donor = await factory.CustomerAsync();
        await factory.PaidOrderAsync(donor, Shop.Donate(cause));
        await factory.PaidOrderAsync(donor, Shop.Donate(cause));                                             // two orders, one donor
        var before = factory.SmsTo(donor.Mobile!).Count;
        var admin = await AdminAsync();
        var day = factory.Clock.RiyadhToday;

        var (posted, update) = await admin.PostAsync($"/v1/admin/products/{cause}/updates",
            new { titleAr = "تسليم الدفعة الأولى", bodyAr = "تم تسليم 20 جهازًا", unitsDelivered = 20, occurredOn = day, isPublic = true, notifyDonors = true });
        posted.StatusCode.ShouldBe(HttpStatusCode.Created, update.ToString());
        await admin.PostAsync($"/v1/admin/products/{cause}/updates", new { titleAr = "ملاحظة داخلية", occurredOn = day, isPublic = false, notifyDonors = false });
        await factory.DrainOutboxAsync();
        await admin.SendAsync(HttpMethod.Put, $"/v1/admin/cause-updates/{update.GetProperty("id").GetGuid()}",
            new { titleAr = "تسليم الدفعة الأولى (معدّل)", occurredOn = day, isPublic = true, notifyDonors = true });
        await factory.DrainOutboxAsync();

        factory.SmsTo(donor.Mobile!).Count.ShouldBe(before + 1);
        var (_, page) = await factory.Anonymous().SendJsonAsync(HttpMethod.Get, $"/v1/catalog/products/tablet-for-student/updates?v={Guid.NewGuid():N}");
        page.ToString().ShouldNotContain("ملاحظة داخلية");
        page.ToString().ShouldContain("الدفعة الأولى");
    }

    [Fact]
    [Rule("RUL-59")]
    public async Task Purchases_get_an_invoice_gifts_a_donation_receipt_numbered_per_series()
    {
        var customer = await factory.CustomerAsync();
        var mixed = await factory.PaidOrderAsync(customer, Shop.Buy(await factory.ProductIdAsync("python-kids")), Shop.Donate(await factory.ProductIdAsync("school-bag")));
        var giftOnly = await factory.PaidOrderAsync(customer, Shop.Donate(await factory.ProductIdAsync("school-bag")));

        async Task<string[]> DocsAsync(PlacedOrder o) => (await customer.GetAsync($"/v1/me/orders/{o.Reference}")).Body.GetProperty("documents")
            .EnumerateArray().Select(d => d.GetProperty("kind").GetString() + ":" + d.GetProperty("number").GetString()).Order().ToArray();

        var mixedDocs = await DocsAsync(mixed);
        mixedDocs.Length.ShouldBe(2);
        mixedDocs[0].ShouldMatch(@"^invoice:INV-\d{4}-\d{6}$");
        mixedDocs[1].ShouldMatch(@"^receipt:DR-\d{4}-\d{6}$");
        (await DocsAsync(giftOnly)).Single().ShouldStartWith("receipt:DR-");
        var numbers = await factory.ScalarAsync<string>("SELECT string_agg(number, ',' ORDER BY number) FROM store.donation_receipt");
        var seq = numbers.Split(',').Select(n => int.Parse(n[^6..], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        seq.ShouldBe(Enumerable.Range(seq[0], seq.Count).ToList());                                          // gapless
    }

    [Fact]
    [Rule("RUL-49")]
    public async Task Units_awaiting_30_days_alert_staff_and_90_days_escalate_to_the_manager()
    {
        await factory.PaidOrderAsync(await factory.CustomerAsync(), Shop.SponsorUnits(await factory.ProductIdAsync("early-childhood"), 1));
        var start = factory.Clock.UtcNow;
        try
        {
            factory.Clock.Advance(TimeSpan.FromDays(31));
            await factory.RunAsync<RaiseAllocationAlertsCommand, int>(new RaiseAllocationAlertsCommand());
            factory.Clock.Advance(TimeSpan.FromDays(60));
            await factory.RunAsync<RaiseAllocationAlertsCommand, int>(new RaiseAllocationAlertsCommand());
        }
        finally
        {
            factory.Clock.Set(start);
        }

        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.outbox_message WHERE type = 'AllocationAlert'")).ShouldBeGreaterThanOrEqualTo(1);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.outbox_message WHERE type = 'AllocationEscalation'")).ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    [Rule("RUL-51")]
    public async Task Beneficiaries_get_a_code_contact_details_are_encrypted_and_creation_is_audited()
    {
        var admin = await AdminAsync();
        var mobile = ScenarioFactory.NewMobile();
        var (created, b) = await admin.PostAsync("/v1/admin/beneficiaries", new
        {
            kind = "library",
            beneficiaryGroup = "public_primary_libraries",
            region = "حائل",
            fullName = "مكتبة المدرسة الأولى",
            mobile,
            organisationName = "مدرسة",
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, b.ToString());
        b.GetProperty("code").GetString()!.ShouldMatch(@"^B-\d{6}$");
        b.GetProperty("eligibilityStatus").GetString().ShouldBe("pending");
        var raw = await factory.ScalarAsync<string>($"SELECT row_to_json(x)::text FROM store.beneficiary x WHERE id = {Q(b.GetProperty("id").GetGuid())}");
        raw.ShouldNotContain("مكتبة المدرسة الأولى");
        raw.ShouldNotContain(mobile[1..]);
        var audit = await factory.ScalarAsync<string>($"SELECT coalesce(string_agg(diff::text, ' '), '') FROM store.audit_log WHERE entity_id = '{b.GetProperty("id").GetGuid()}'");
        audit.ShouldNotBeEmpty();
        audit.ShouldNotContain(mobile[1..]);
    }
}
