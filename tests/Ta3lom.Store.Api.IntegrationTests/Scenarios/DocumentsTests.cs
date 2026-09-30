using System.Globalization;
using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.Services;
using UglyToad.PdfPig;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace Ta3lom.Store.Api.IntegrationTests.Scenarios;

/// T-8 — documents and outputs (plan 14 S-17, S-20, S-31): PDF text and Arabic, the ZATCA QR decoded from the PDF image,
/// gapless numbering under parallel settlements, the watermark, outbox retries, template fallback, donor DTOs.
public sealed class DocumentsTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static bool HasArabic(string text) => text.Any(c => c is >= '؀' and <= 'ۿ' or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿');

    private static string Text(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return string.Join("\n", doc.GetPages().Select(p => p.Text));
    }

    private async Task<byte[]> DocumentAsync(ActorClient customer, string number)
    {
        var response = await customer.Http.GetAsync($"/v1/me/documents/{number}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsByteArrayAsync(Ct);
    }

    [Fact]
    [Rule("RUL-59")]
    [Rule("RUL-27")]
    public async Task A_tax_invoice_carries_its_numbers_Arabic_text_and_a_ZATCA_QR_that_decodes_to_the_five_fields()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        await admin.SendAsync(HttpMethod.Put, "/v1/admin/settings/seller_vat_no", new { value = "300000000000003" });
        await admin.SendAsync(HttpMethod.Put, "/v1/admin/settings/vat_enabled", new { value = true });
        try
        {
            var customer = await factory.CustomerAsync();
            var order = await factory.PaidOrderAsync(customer, Shop.Buy(await factory.ProductIdAsync("digital-teacher")), Shop.Donate(await factory.ProductIdAsync("school-bag"), 1, "zakat"));
            var docs = (await customer.GetAsync($"/v1/me/orders/{order.Reference}")).Body.GetProperty("documents").EnumerateArray()
                .ToDictionary(d => d.GetProperty("kind").GetString()!, d => d.GetProperty("number").GetString()!);

            var invoicePdf = await DocumentAsync(customer, docs["invoice"]);
            var invoiceText = Text(invoicePdf);
            invoiceText.ShouldContain(docs["invoice"]);
            invoiceText.ShouldContain(order.Reference);
            invoiceText.ShouldContain("350.00");
            invoiceText.ShouldContain("45.65");                                                           // VAT 15 % of 350.00 inclusive
            HasArabic(invoiceText).ShouldBeTrue();

            var stored = await factory.ScalarAsync<string>($"SELECT zatca_qr FROM store.invoice WHERE number = '{docs["invoice"]}'");
            var decoded = DecodeQr(invoicePdf);
            decoded.ShouldBe(stored);
            var fields = ZatcaQr.Decode(decoded);
            fields.Keys.Order().ShouldBe([1, 2, 3, 4, 5]);
            fields[2].ShouldBe("300000000000003");
            fields[4].ShouldBe("350.00");
            fields[5].ShouldBe("45.65");
            DateTimeOffset.Parse(fields[3], CultureInfo.InvariantCulture).ShouldBeInRange(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));

            var receiptText = Text(await DocumentAsync(customer, docs["receipt"]));
            receiptText.ShouldContain(docs["receipt"]);
            receiptText.ShouldContain("180.00");
            HasArabic(receiptText).ShouldBeTrue();
        }
        finally
        {
            await admin.SendAsync(HttpMethod.Put, "/v1/admin/settings/vat_enabled", new { value = false });
            await admin.SendAsync(HttpMethod.Put, "/v1/admin/settings/seller_vat_no", new { value = (string?)null });
        }
    }

    /// The QR is a PNG inside the PDF: extract it with PdfPig, read the pixels with SkiaSharp, decode with ZXing.
    private static string DecodeQr(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        foreach (var image in doc.GetPages().SelectMany(p => p.GetImages()))
        {
            var bytes = image.TryGetPng(out var png) ? png : image.RawBytes.ToArray();
            using var bitmap = SKBitmap.Decode(bytes);
            if (bitmap is null)
            {
                continue;
            }

            using var rgba = bitmap.Copy(SKColorType.Rgba8888);
            var source = new RGBLuminanceSource(rgba.Bytes, rgba.Width, rgba.Height, RGBLuminanceSource.BitmapFormat.RGBA32);
            var result = new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(source)));
            if (result is not null)
            {
                return result.Text;
            }
        }

        throw new ShouldAssertException("No QR code found in the invoice PDF.");
    }

    [Fact]
    [Rule("RUL-59")]
    public async Task Numbers_stay_gapless_under_20_parallel_settlements()
    {
        var course = await factory.ProductIdAsync("smart-study");
        var cause = await factory.ProductIdAsync("water-for-schools");
        var orders = new List<PlacedOrder>();
        for (var i = 0; i < 20; i++)
        {
            orders.Add(await (await factory.CustomerAsync()).OrderAsync(Shop.Buy(course), Shop.Donate(cause)));
        }

        var invBefore = await factory.ScalarAsync<long>("SELECT count(*) FROM store.invoice");
        var drBefore = await factory.ScalarAsync<long>("SELECT count(*) FROM store.donation_receipt");
        await Task.WhenAll(orders.Select(o => factory.SettleAsync(o.Reference)));

        foreach (var table in new[] { "invoice", "donation_receipt" })
        {
            var numbers = (await factory.ScalarAsync<string>($"SELECT string_agg(number, ',' ORDER BY number) FROM store.{table}")).Split(',')
                .Select(n => int.Parse(n[^6..], CultureInfo.InvariantCulture)).ToList();
            numbers.ShouldBe(Enumerable.Range(1, numbers.Count).ToList(), table);                   // 1 … n, no gap, no duplicate
        }

        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.invoice")).ShouldBe(invBefore + 20);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.donation_receipt")).ShouldBe(drBefore + 20);
    }

    [Fact]
    [Rule("RUL-41")]
    public async Task Every_download_is_stamped_with_the_buyer_masked_mobile_and_order_reference()
    {
        var admin = await factory.StaffAsync(Roles.Admin, Roles.StoreManager);
        const string book = "formative-guide";
        var digital = await factory.FormatIdAsync(book, "digital");
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent("book_file"), "kind");
            var file = new ByteArrayContent(ScenarioFactory.MinimalPdf());
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            form.Add(file, "file", "guide.pdf");
            var uploaded = await admin.Http.PostAsync("/v1/admin/media", form, Ct);
            var asset = System.Text.Json.JsonDocument.Parse(await uploaded.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("id").GetGuid();
            await factory.ExecuteSqlAsync($"UPDATE store.book_format SET file_asset_id = '{asset}' WHERE id = '{digital}'");
        }

        var buyer = await factory.CustomerAsync();
        var order = await factory.PaidOrderAsync(buyer, Shop.Buy(await factory.ProductIdAsync(book), 1, digital));
        var grant = (await buyer.GetAsync("/v1/me/books")).Body.GetProperty("downloads")[0].GetProperty("grantId").GetGuid();
        var (_, link) = await buyer.PostAsync($"/v1/me/downloads/{grant}/link");
        var pdf = await (await factory.Anonymous().GetAsync(new Uri(link.GetProperty("url").GetString()!).PathAndQuery, Ct)).Content.ReadAsByteArrayAsync(Ct);

        var text = Text(pdf);
        text.ShouldContain(order.Reference);
        text.ShouldContain(buyer.Mobile![^3..]);                                                         // masked: only the last digits
        text.ShouldNotContain(buyer.Mobile[1..]);
    }

    [Fact]
    [Rule("RUL-63")]
    public async Task An_outbox_retry_never_sends_the_same_SMS_twice()
    {
        var customer = await factory.CustomerAsync();
        var order = await factory.PaidOrderAsync(customer, Shop.Donate(await factory.ProductIdAsync("learning-data")));
        var sent = factory.SmsTo(customer.Mobile!).Count;

        await factory.ExecuteSqlAsync($"UPDATE store.outbox_message SET processed_at = NULL, next_attempt_at = now() WHERE type = 'OrderPaid' AND payload->>'orderId' = '{order.OrderId}'");
        await factory.DrainOutboxAsync();

        factory.SmsTo(customer.Mobile!).Count.ShouldBe(sent);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.notification WHERE related_order_id = '{order.OrderId}' AND channel = 'sms'")).ShouldBe(1);
    }

    [Fact]
    [Rule("RUL-63")]
    public async Task A_missing_English_template_falls_back_to_Arabic()
    {
        await factory.ExecuteSqlAsync("DELETE FROM store.message_template WHERE key = 'shipment_delivered' AND channel = 'sms' AND locale = 'en'");
        await using var scope = factory.Services.CreateAsyncScope();

        var message = await scope.ServiceProvider.GetRequiredService<ITemplateRenderer>()
            .RenderAsync("shipment_delivered", MessageChannel.Sms, "en", new { reference = "T3-26-000001" }, Ct);

        message.Body.ShouldBe("تم تسليم طلبك T3-26-000001. شكراً لك.");
    }

    [Fact]
    [Rule("RUL-50")]
    public void Donor_facing_DTOs_have_no_beneficiary_identity_fields()
    {
        var donorFacing = typeof(Ta3lom.Store.Application.DTOs.Account.GivingResponse).Assembly.GetTypes()
            .Where(t => t.Namespace is "Ta3lom.Store.Application.DTOs.Account" or "Ta3lom.Store.Application.DTOs.Catalog"
                        && (t.Name.StartsWith("Giving", StringComparison.Ordinal) || t.Name.Contains("Feed", StringComparison.Ordinal)
                            || t.Name.Contains("Update", StringComparison.Ordinal) || t.Name.Contains("Sponsorship", StringComparison.Ordinal))
                        && !t.Name.EndsWith("Request", StringComparison.Ordinal) && !t.Name.EndsWith("Input", StringComparison.Ordinal))   // what donors are shown
            .ToList();
        donorFacing.Count.ShouldBeGreaterThan(8);
        string[] forbidden = ["Beneficiary", "FullName", "Mobile", "Guardian", "NationalId"];

        donorFacing.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => $"{t.Name}.{p.Name}"))
            .Where(n => forbidden.Any(f => n.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ShouldBeEmpty();
    }

    [Fact]
    [Rule("RUL-50")]
    public async Task A_sponsors_giving_view_shows_status_region_and_group_but_no_beneficiary()
    {
        var product = await factory.ProductIdAsync("python-kids");
        var sponsor = await factory.CustomerAsync();
        var order = await factory.PaidOrderAsync(sponsor, Shop.SponsorUnits(product, 1));
        var admin = await factory.StaffAsync(Roles.Admin);
        var mobile = ScenarioFactory.NewMobile();
        var (_, b) = await admin.PostAsync("/v1/admin/beneficiaries", new
        {
            kind = "student",
            beneficiaryGroup = "orphan_home_students",
            region = "عسير",
            fullName = "طالب يتيم سري",
            mobile,
            guardianName = "مشرف الدار",
            organisationName = "دار",
        });
        await admin.PostAsync($"/v1/admin/beneficiaries/{b.GetProperty("id").GetGuid()}/eligibility", new { status = "approved", zakatEligible = false });
        await (await factory.StaffAsync(Roles.AllocationOfficer)).PostAsync($"/v1/admin/allocation/products/{product}/allocate", new { beneficiaryIds = new[] { b.GetProperty("id").GetGuid() } });

        var line = await factory.ScalarAsync<Guid>($"SELECT id FROM store.order_line WHERE order_id = '{order.OrderId}'");
        var (_, detail) = await sponsor.GetAsync($"/v1/me/giving/lines/{line}");
        var (_, summary) = await sponsor.GetAsync("/v1/me/giving");
        var json = detail.ToString() + summary;

        detail.GetProperty("units")[0].GetProperty("status").GetString().ShouldBe("allocated");
        detail.GetProperty("units")[0].GetProperty("region").GetString().ShouldBe("عسير");
        json.ShouldNotContain("طالب يتيم");
        json.ShouldNotContain("مشرف الدار");
        json.ShouldNotContain(mobile[1..]);
        json.ShouldNotContain(b.GetProperty("code").GetString()!);
    }
}
