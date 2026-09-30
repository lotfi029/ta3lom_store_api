using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SkiaSharp;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Features.Fulfilment.Commands.LowStockAlert;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.Scenarios;

/// T-7 — success paths of the admin routes not driven by the rule classes: catalogue lifecycle (#54 … #77), goal
/// corrections (#136, #137), customers and audit (#129 … #133), reports (#117), allocation and beneficiary queries,
/// stock and shipments (#78 … #80, #95 … #99), the low-stock job and templates (#120 … #122).
public sealed class AdminFlowsTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(64, 48);
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static async Task<JsonElement> UploadAsync(ActorClient staff, string kind, byte[] content, string type, string name)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(kind), "kind");
        form.Add(new StringContent("غلاف تجربة"), "altAr");
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "file", name);
        var response = await staff.Http.PostAsync("/v1/admin/media", form, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }

    [Fact]
    [Rule("RUL-02")]
    public async Task A_course_goes_from_draft_through_publish_to_archive()
    {
        var manager = await factory.StaffAsync(Roles.StoreManager);
        var (_, category) = await manager.PostAsync("/v1/admin/categories", new { slug = "t7-cat", nameAr = "فئة", sort = 5, isActive = true });
        var categoryId = category.GetProperty("id").GetGuid();
        (await manager.SendAsync(HttpMethod.Put, $"/v1/admin/categories/{categoryId}", new { slug = "t7-cat", nameAr = "فئة معدلة", sort = 6, isActive = true }))
            .Response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var (_, p) = await manager.PostAsync("/v1/admin/products", new { type = "course", slug = "t7-course", titleAr = "دورة تجربة", summaryAr = "ملخص", sort = 1, categoryId });
        var id = p.GetProperty("id").GetGuid();
        (await manager.SendAsync(HttpMethod.Put, $"/v1/admin/products/{id}/course", new { price = "199.00", lessons = 4, durationMinutes = 90, instructorAr = "مدرب", learnerCount = 0 }))
            .Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await manager.SendAsync(HttpMethod.Put, $"/v1/admin/products/{id}/modules", new[]
        {
            new { sort = 1, titleAr = "المقدمة", durationSeconds = 600 }, new { sort = 2, titleAr = "التطبيق", durationSeconds = 1200 },
        })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await manager.PostAsync($"/v1/admin/products/{id}/publish")).Body.Code().ShouldBe("product.publish_incomplete");

        var cover = await UploadAsync(manager, "image", Png(), "image/png", "cover.png");
        (await manager.SendAsync(HttpMethod.Patch, $"/v1/admin/media/{cover.GetProperty("id").GetGuid()}", new { altAr = "غلاف الدورة", altEn = "Course cover" }))
            .Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await manager.SendAsync(HttpMethod.Put, $"/v1/admin/products/{id}", new
        {
            type = "course",
            slug = "t7-course",
            titleAr = "دورة تجربة",
            summaryAr = "ملخص",
            sort = 1,
            categoryId,
            coverAssetId = cover.GetProperty("id").GetGuid(),
        })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await manager.GetAsync($"/v1/admin/products/{id}/preview")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await manager.PostAsync($"/v1/admin/products/{id}/publish")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var (_, detail) = await factory.Anonymous().SendJsonAsync(HttpMethod.Get, $"/v1/catalog/products/t7-course?v={Guid.NewGuid():N}");
        detail.GetProperty("course").GetProperty("modules").GetArrayLength().ShouldBe(2);
        detail.GetProperty("cover").GetProperty("url").GetString().ShouldNotBeNull();
        (await manager.GetAsync("/v1/admin/products?status=published&type=course")).Body.GetProperty("items").EnumerateArray()
            .ShouldContain(i => i.GetProperty("slug").GetString() == "t7-course");
        (await manager.GetAsync("/v1/admin/media")).Body.GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);
        (await manager.PostAsync($"/v1/admin/products/{id}/archive")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await factory.Anonymous().SendJsonAsync(HttpMethod.Get, $"/v1/catalog/products/t7-course?v={Guid.NewGuid():N}")).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Rule("RUL-05")]
    public async Task A_book_gets_details_formats_and_a_sponsorship_offer_that_can_be_changed_and_closed()
    {
        var manager = await factory.StaffAsync(Roles.StoreManager);
        var (_, p) = await manager.PostAsync("/v1/admin/products", new { type = "book", slug = "t7-book", titleAr = "كتاب تجربة", summaryAr = "ملخص", sort = 1 });
        var id = p.GetProperty("id").GetGuid();
        (await manager.SendAsync(HttpMethod.Put, $"/v1/admin/products/{id}/book", new { authorAr = "مؤلف", pages = 120, publishedYear = 2026 })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (printAdded, print) = await manager.PostAsync($"/v1/admin/products/{id}/formats", new { kind = "print", sku = "T7-BOOK-P", price = "60.00", isActive = true, weightGrams = 300 });
        printAdded.StatusCode.ShouldBe(HttpStatusCode.Created, print.ToString());
        var file = await UploadAsync(manager, "book_file", ScenarioFactory.MinimalPdf(), "application/pdf", "book.pdf");
        (await manager.PostAsync($"/v1/admin/products/{id}/formats", new { kind = "digital", sku = "T7-BOOK-D", price = "30.00", isActive = true, fileAssetId = file.GetProperty("id").GetGuid() }))
            .Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await manager.SendAsync(HttpMethod.Put, $"/v1/admin/formats/{print.GetProperty("id").GetGuid()}", new { kind = "print", sku = "T7-BOOK-P", price = "65.00", compareAtPrice = "80.00", isActive = true, weightGrams = 320, lowStockThreshold = 5 }))
            .Body.GetProperty("price").GetString().ShouldBe("65.00");

        var (offered, offer) = await manager.PostAsync($"/v1/admin/products/{id}/offers", new
        {
            seatPrice = "40.00",
            unitLabelAr = "نسخة",
            targetUnits = 50,
            beneficiaryGroup = "public_primary_libraries",
            groupLabelAr = "مكتبات المدارس",
            allowPartial = true,
            zakatEligible = false,
            closeOnTarget = false,
            startsAtLocal = "2026-09-01T00:00:00",
            endsAtLocal = "2027-03-31T23:59:00",
        });
        offered.StatusCode.ShouldBe(HttpStatusCode.Created, offer.ToString());
        var offerId = offer.GetProperty("id").GetGuid();
        (await manager.SendAsync(HttpMethod.Put, $"/v1/admin/offers/{offerId}", new
        {
            seatPrice = "45.00",
            unitLabelAr = "نسخة",
            targetUnits = 60,
            beneficiaryGroup = "public_primary_libraries",
            groupLabelAr = "مكتبات المدارس",
            allowPartial = true,
            zakatEligible = false,
            closeOnTarget = false,
            startsAtLocal = "2026-09-01T00:00:00",
            endsAtLocal = "2027-03-31T23:59:00",
        })).Body.GetProperty("seatPrice").GetString().ShouldBe("45.00");
        (await manager.GetAsync($"/v1/admin/products/{id}/offers")).Body.GetArrayLength().ShouldBe(1);
        var (closed, close) = await manager.PostAsync($"/v1/admin/offers/{offerId}/close", new { sweepPool = true });
        closed.StatusCode.ShouldBe(HttpStatusCode.OK, close.ToString());
        close.GetProperty("offer").GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    [Rule("RUL-08")]
    public async Task Goal_seeds_and_finance_corrections_move_the_counters_with_an_audit_trail()
    {
        var finance = await factory.StaffAsync(Roles.Finance, Roles.StoreManager);
        var cause = await factory.ProductIdAsync("water-for-schools");
        (await finance.SendAsync(HttpMethod.Put, $"/v1/admin/products/{cause}/goal-seed", new { seedRaised = "3000.00", seedUnits = 2, seedDonors = 5, reason = "افتتاحي" }))
            .Response.IsSuccessStatusCode.ShouldBeTrue();
        var (made, correction) = await finance.PostAsync($"/v1/admin/products/{cause}/goal-adjustments", new { amount = "100.00", units = 0, note = "تحويل غير مسجل" });
        made.StatusCode.ShouldBe(HttpStatusCode.Created, correction.ToString());
        (await finance.PostAsync($"/v1/admin/products/{cause}/goal-adjustments", new { amount = "-40.00", units = 0, note = "تصحيح إدخال" })).Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await finance.PostAsync($"/v1/admin/products/{cause}/goal-adjustments", new { amount = "-1000000.00", units = 0, note = "سالب" })).Body.Code().ShouldBe("goal.adjustment_invalid");
        (await finance.GetAsync($"/v1/admin/products/{cause}/goal-adjustments")).Body.GetProperty("items").GetArrayLength().ShouldBeGreaterThanOrEqualTo(2);
        (await factory.IntegrityFailuresAsync()).ShouldBeEmpty();
    }

    [Fact]
    [Rule("RUL-61")]
    public async Task Staff_find_a_customer_see_the_360_view_block_unblock_and_read_the_audit_log()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        var mobile = ScenarioFactory.NewMobile();
        var customer = await factory.CustomerAsync(mobile);
        await factory.PaidOrderAsync(customer, Shop.Donate(await factory.ProductIdAsync("school-bag")));

        var (_, found) = await admin.GetAsync($"/v1/admin/customers?mobile={mobile}");
        found.GetArrayLength().ShouldBe(1);
        var (_, view) = await admin.GetAsync($"/v1/admin/customers/{customer.Id}");
        view.ToString().ShouldContain("orders", Case.Insensitive);
        (await admin.PostAsync($"/v1/admin/customers/{customer.Id}/block", new { reason = "مراجعة" })).Body.GetProperty("status").GetString().ShouldBe("blocked");
        (await admin.PostAsync($"/v1/admin/customers/{customer.Id}/unblock")).Body.GetProperty("status").GetString().ShouldBe("active");
        (await admin.PostAsync($"/v1/admin/customers/{customer.Id}/block", new { reason = "" })).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var (_, audit) = await admin.GetAsync($"/v1/admin/audit?entity=customer&entityId={customer.Id}");
        audit.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("action").GetString()).ShouldContain("customer.block");
        (await admin.GetAsync($"/v1/admin/customers/{Guid.NewGuid()}")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("sales")]
    [InlineData("vat")]
    [InlineData("gifts")]
    [InlineData("zakat")]
    [InlineData("allocation")]
    [InlineData("refunds")]
    [InlineData("shipments")]
    public async Task Every_report_answers_as_JSON_and_as_CSV(string name)
    {
        var finance = await factory.StaffAsync(Roles.Finance);
        var day = factory.Clock.RiyadhToday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var (json, report) = await finance.GetAsync($"/v1/admin/reports/{name}?from={day}&to={day}");
        json.StatusCode.ShouldBe(HttpStatusCode.OK);
        report.GetProperty("name").GetString().ShouldBe(name);
        var csv = await finance.Http.GetAsync($"/v1/admin/reports/{name}?from={day}&to={day}&format=csv", Ct);
        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await csv.Content.ReadAsByteArrayAsync(Ct))[..3].ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
        (await finance.GetAsync($"/v1/admin/reports/{name}?from={day}&to=2020-01-01")).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_reports_are_not_found_and_the_dashboard_answers()
    {
        var finance = await factory.StaffAsync(Roles.Finance, Roles.Viewer);
        (await finance.GetAsync("/v1/admin/reports/nope?from=2026-01-01&to=2026-01-02")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var (_, dashboard) = await finance.GetAsync("/v1/admin/dashboard");
        dashboard.ValueKind.ShouldBe(JsonValueKind.Object);
    }

    [Fact]
    [Rule("RUL-47")]
    public async Task Officers_see_the_backlog_units_candidates_batches_and_complete_a_book_copy()
    {
        var product = await factory.ProductIdAsync("science-stars-book");                               // book copies for libraries
        await factory.PaidOrderAsync(await factory.CustomerAsync(), Shop.SponsorUnits(product, 1));
        var admin = await factory.StaffAsync(Roles.Admin);
        var officer = await factory.StaffAsync(Roles.AllocationOfficer);
        var (_, b) = await admin.PostAsync("/v1/admin/beneficiaries", new { kind = "library", beneficiaryGroup = "public_primary_libraries", region = "الرياض", fullName = "مكتبة ابتدائية", organisationName = "مدرسة" });
        var bId = b.GetProperty("id").GetGuid();
        (await admin.SendAsync(HttpMethod.Put, $"/v1/admin/beneficiaries/{bId}", new { kind = "library", beneficiaryGroup = "public_primary_libraries", region = "الرياض", fullName = "مكتبة ابتدائية ٢", organisationName = "مدرسة" }))
            .Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await admin.PostAsync($"/v1/admin/beneficiaries/{bId}/eligibility", new { status = "approved", zakatEligible = false });
        (await admin.GetAsync("/v1/admin/beneficiaries?group=public_primary_libraries&status=approved")).Body.GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);
        (await admin.GetAsync($"/v1/admin/beneficiaries/{bId}")).Body.GetProperty("code").GetString()!.ShouldStartWith("B-");
        (await admin.PostAsync($"/v1/admin/beneficiaries/{bId}/invite")).Body.Code().ShouldBe("beneficiary.no_live_seat");

        (await officer.GetAsync("/v1/admin/allocation/backlog")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await officer.GetAsync($"/v1/admin/allocation/products/{product}/units?status=awaiting_allocation")).Body.GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);
        (await officer.GetAsync($"/v1/admin/allocation/products/{product}/candidates")).Body.ToString().ShouldContain(b.GetProperty("code").GetString()!);
        var (_, allocated) = await officer.PostAsync($"/v1/admin/allocation/products/{product}/allocate", new { beneficiaryIds = new[] { bId } });
        (await officer.GetAsync($"/v1/admin/allocation/batches/{allocated.GetProperty("batchId").GetGuid()}")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var unit = allocated.GetProperty("units")[0].GetProperty("unitId").GetGuid();
        var (completed, done) = await officer.PostAsync($"/v1/admin/units/{unit}/complete", new { deliveredOn = factory.Clock.RiyadhToday });
        completed.StatusCode.ShouldBe(HttpStatusCode.OK, done.ToString());
        (await factory.ScalarAsync<string>($"SELECT status FROM store.sponsored_unit WHERE id = '{unit}'")).ShouldBe("completed");
        (await factory.ScalarAsync<bool>($"SELECT delivered_on IS NOT NULL FROM store.distribution WHERE sponsored_unit_id = '{unit}'")).ShouldBeTrue();
        (await officer.PostAsync($"/v1/admin/units/{unit}/complete", new { deliveredOn = factory.Clock.RiyadhToday })).Body.Code().ShouldBe("unit.invalid_transition");
    }

    [Fact]
    [Rule("RUL-30")]
    public async Task Stock_is_listed_moved_and_a_shipment_can_be_returned_with_restock_and_a_packing_slip()
    {
        var fulfilment = await factory.StaffAsync(Roles.Fulfilment);
        var print = await factory.FormatIdAsync("formative-guide", "print");
        (await fulfilment.GetAsync("/v1/admin/stock?lowOnly=false")).Body.EnumerateArray().ShouldContain(s => s.GetProperty("formatId").GetGuid() == print);
        (await fulfilment.PostAsync($"/v1/admin/stock/{print}/movements", new { delta = 10, reason = "restock", note = "شحنة" })).Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await fulfilment.PostAsync($"/v1/admin/stock/{print}/movements", new { delta = -99999, reason = "adjustment", note = "خطأ" })).Body.Code().ShouldBe("stock.below_reserved");
        (await fulfilment.GetAsync($"/v1/admin/stock/{print}/movements")).Body.GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);

        var customer = await factory.CustomerAsync();
        var order = await factory.PaidOrderAsync(customer, Shop.Buy(await factory.ProductIdAsync("formative-guide"), 1, print));
        var shipment = await factory.ScalarAsync<Guid>($"SELECT id FROM store.shipment WHERE order_id = '{order.OrderId}'");
        var slip = await fulfilment.Http.GetAsync($"/v1/admin/shipments/{shipment}/packing-slip", Ct);
        slip.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        (await fulfilment.PostAsync($"/v1/admin/shipments/{shipment}/return", new { note = "عاد", restock = true })).Body.Code().ShouldBe("shipment.invalid_transition");
        await fulfilment.PostAsync($"/v1/admin/shipments/{shipment}/ship", new { carrier = "SMSA", trackingNo = "T7-RET" });
        (await fulfilment.GetAsync("/v1/admin/shipments?status=shipped")).Body.GetProperty("items").EnumerateArray().ShouldContain(s => s.GetProperty("id").GetGuid() == shipment);
        var before = await factory.ScalarAsync<int>($"SELECT stock_on_hand FROM store.book_format WHERE id = '{print}'");
        (await fulfilment.PostAsync($"/v1/admin/shipments/{shipment}/return", new { note = "العنوان خطأ", restock = true })).Body.GetProperty("status").GetString().ShouldBe("returned");
        (await factory.ScalarAsync<int>($"SELECT stock_on_hand FROM store.book_format WHERE id = '{print}'")).ShouldBe(before + 1);
        (await factory.IntegrityFailuresAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Low_stock_raises_one_staff_alert_per_format_per_day()
    {
        var print = await factory.FormatIdAsync("study-planner", "print");
        var onHand = await factory.ScalarAsync<int>($"SELECT stock_on_hand FROM store.book_format WHERE id = '{print}'");
        await factory.ExecuteSqlAsync($"UPDATE store.book_format SET stock_on_hand = stock_reserved + 2 WHERE id = '{print}'");
        try
        {
            (await factory.RunAsync<LowStockAlertCommand, int>(new LowStockAlertCommand())).Value.ShouldBeGreaterThanOrEqualTo(1);
            (await factory.RunAsync<LowStockAlertCommand, int>(new LowStockAlertCommand())).Value.ShouldBe(0);
            await factory.DrainOutboxAsync();
            (await factory.MailAsync()).ShouldContain(m => m.Subject.Contains("BK-STUDY-PLANNE-P", StringComparison.Ordinal) || m.Body.Contains("BK-STUDY-PLANNE-P", StringComparison.Ordinal));
        }
        finally
        {
            await factory.ExecuteSqlAsync($"UPDATE store.book_format SET stock_on_hand = {onHand} WHERE id = '{print}'");
        }
    }

    [Fact]
    [Rule("RUL-63")]
    public async Task Templates_are_listed_with_their_variables_and_a_test_message_is_sent()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        var (_, list) = await admin.GetAsync("/v1/admin/templates");
        list.EnumerateArray().ShouldContain(t => t.GetProperty("key").GetString() == "order_paid" && t.GetProperty("variables").GetArrayLength() == 3);
        var mobile = ScenarioFactory.NewMobile();

        var (sent, body) = await admin.PostAsync("/v1/admin/templates/order_paid/sms/ar/test", new { recipient = mobile, sampleData = new Dictionary<string, string> { ["reference"] = "T3-26-000009" } });

        sent.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        body.GetProperty("rendered").GetProperty("body").GetString()!.ShouldContain("T3-26-000009");
        factory.SmsTo(mobile).Single().Body.ShouldContain("[total]");                                    // unset variables are shown
        (await admin.PostAsync("/v1/admin/templates/order_paid/email/ar/test", new { recipient = "ops@example.test" })).Response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await admin.PostAsync("/v1/admin/templates/order_paid/sms/ar/test", new { recipient = "not-a-mobile" })).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/v1/admin/templates/nope/sms/ar/test", new { recipient = mobile })).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
