using System.Net;
using System.Text.Json;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.Scenarios;

/// T-5 — catalogue rules (plan 14 S-08, S-09: RUL-01 … RUL-10, RUL-23, RUL-44, RUL-55).
public sealed class CatalogueRulesTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private ActorClient? _manager;

    private async Task<ActorClient> ManagerAsync() => _manager ??= await factory.StaffAsync(Roles.StoreManager);

    /// Public reads with a unique query string: the output cache varies by query, and SQL edits do not evict it.
    private async Task<(HttpStatusCode Status, JsonElement Body)> FreshAsync(string path) =>
        await factory.Anonymous().SendJsonAsync(HttpMethod.Get, $"{path}{(path.Contains('?') ? '&' : '?')}v={Guid.NewGuid():N}");

    private static object Product(string type, string slug, string? titleAr = "منتج تجربة") => new
    {
        type,
        slug,
        titleAr,
        summaryAr = "ملخص",
        sort = 0,
    };

    [Fact]
    [Rule("RUL-01")]
    public async Task The_type_is_locked_after_the_first_order()
    {
        var manager = await ManagerAsync();
        var id = await factory.ProductIdAsync("qudrat-prep");
        await (await factory.CustomerAsync()).OrderAsync(Shop.Buy(id));
        var (_, p) = await manager.GetAsync($"/v1/admin/products/{id}");
        p.GetProperty("typeLocked").GetBoolean().ShouldBeTrue();

        var (status, problem) = await manager.SendAsync(HttpMethod.Put, $"/v1/admin/products/{id}", new
        {
            type = "book",
            slug = p.GetProperty("slug").GetString(),
            titleAr = p.GetProperty("titleAr").GetString(),
            summaryAr = p.GetProperty("summaryAr").GetString(),
            sort = p.GetProperty("sort").GetInt32(),
        });

        status.StatusCode.ShouldBeOneOf(HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity);
        problem.Code().ShouldBe("product.type_locked");
        (await manager.PostAsync("/v1/admin/products", Product("unknown", "t-bad-type"))).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Rule("RUL-02")]
    public async Task Drafts_are_admin_only_and_archived_products_stay_in_history_but_cannot_be_bought()
    {
        var manager = await ManagerAsync();
        var (created, draft) = await manager.PostAsync("/v1/admin/products", Product("course", "t-draft-course"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, draft.ToString());
        (await FreshAsync("/v1/catalog/products/t-draft-course")).Status.ShouldBe(HttpStatusCode.NotFound);
        (await FreshAsync("/v1/catalog/products?fields=slug&pageSize=100")).Body.GetProperty("items").EnumerateArray()
            .ShouldNotContain(i => i.GetProperty("slug").GetString() == "t-draft-course");
        (await manager.GetAsync($"/v1/admin/products/{draft.GetProperty("id").GetGuid()}/preview")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var buyer = await factory.CustomerAsync();
        var book = await factory.ProductIdAsync("formative-guide");
        var order = await factory.PaidOrderAsync(buyer, Shop.Buy(book, 1, await factory.FormatIdAsync("formative-guide", "digital")));
        (await manager.PostAsync($"/v1/admin/products/{book}/archive")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        try
        {
            (await FreshAsync("/v1/catalog/products/formative-guide")).Status.ShouldBe(HttpStatusCode.NotFound);
            var (added, _) = await (await factory.CustomerAsync()).PostAsync("/v1/cart/lines", Shop.Buy(book, 1, await FormatAsync(book)));
            added.IsSuccessStatusCode.ShouldBeFalse();
            var (_, mine) = await buyer.GetAsync($"/v1/me/orders/{order.Reference}");
            mine.GetProperty("lines")[0].GetProperty("titleAr").GetString().ShouldNotBeNullOrEmpty();          // still in history
        }
        finally
        {
            await factory.ExecuteSqlAsync($"UPDATE store.product SET status = 'published' WHERE id = '{book}'");   // the seed has no cover to re-publish with
        }
    }

    private async Task<Guid> FormatAsync(Guid product) =>
        (await (await ManagerAsync()).GetAsync($"/v1/admin/products/{product}")).Body.GetProperty("formats").EnumerateArray()
            .First(f => f.GetProperty("kind").GetString() == "digital").GetProperty("id").GetGuid();

    [Fact]
    [Rule("RUL-03")]
    public async Task Arabic_fields_are_mandatory_English_is_optional()
    {
        var manager = await ManagerAsync();

        (await manager.PostAsync("/v1/admin/products", Product("cause", "t-no-arabic", titleAr: null))).Body.Code().ShouldBe("validation.failed");
        var (created, body) = await manager.PostAsync("/v1/admin/products", Product("cause", "t-arabic-only"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        body.GetProperty("titleEn").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    [Rule("RUL-04")]
    public async Task A_compare_at_price_must_be_higher_than_the_price()
    {
        var manager = await ManagerAsync();
        var (_, p) = await manager.PostAsync("/v1/admin/products", Product("course", "t-course-price"));
        var id = p.GetProperty("id").GetGuid();
        object Course(string price, string? compareAt) => new { price, compareAtPrice = compareAt, lessons = 5, durationMinutes = 60, instructorAr = "مدرب", learnerCount = 0 };

        (await manager.SendAsync(HttpMethod.Put, $"/v1/admin/products/{id}/course", Course("200.00", "150.00"))).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await manager.SendAsync(HttpMethod.Put, $"/v1/admin/products/{id}/course", Course("200.00", "200.00"))).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await manager.SendAsync(HttpMethod.Put, $"/v1/admin/products/{id}/course", Course("200.00", "250.00"))).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Rule("RUL-05")]
    public async Task Only_print_formats_have_stock_digital_needs_a_file()
    {
        var book = await factory.ProductAsync("science-stars-book");
        var formats = book.GetProperty("book").GetProperty("formats").EnumerateArray().ToDictionary(f => f.GetProperty("kind").GetString()!);
        formats["print"].GetProperty("available").ValueKind.ShouldBe(JsonValueKind.Number);
        formats["print"].GetProperty("maxQuantity").GetInt32().ShouldBe(20);
        formats["digital"].GetProperty("available").ValueKind.ShouldBe(JsonValueKind.Null);
        formats["digital"].GetProperty("maxQuantity").GetInt32().ShouldBe(1);

        var manager = await ManagerAsync();
        var (_, p) = await manager.PostAsync("/v1/admin/products", Product("book", "t-book-formats"));
        var (noFile, _) = await manager.PostAsync($"/v1/admin/products/{p.GetProperty("id").GetGuid()}/formats",
            new { kind = "digital", sku = "T-BOOK-D", price = "20.00", isActive = true });
        noFile.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Rule("RUL-06")]
    public async Task One_active_offer_per_product_with_valid_dates_and_no_zakat_in_S1()
    {
        var manager = await ManagerAsync();
        var id = await factory.ProductIdAsync("python-kids");
        object Offer(bool zakat = false, string ends = "2027-06-30T23:59:00") => new
        {
            seatPrice = "300.00",
            unitLabelAr = "مقعد",
            targetUnits = 10,
            beneficiaryGroup = "orphan_home_students",
            groupLabelAr = "أيتام",
            allowPartial = true,
            zakatEligible = zakat,
            closeOnTarget = false,
            startsAtLocal = "2026-10-01T00:00:00",
            endsAtLocal = ends,
        };

        (await manager.PostAsync($"/v1/admin/products/{id}/offers", Offer())).Body.Code().ShouldBe("offer.active_exists");

        var (_, fresh) = await manager.PostAsync("/v1/admin/products", Product("course", "t-course-offer"));
        var freshId = fresh.GetProperty("id").GetGuid();
        (await manager.PostAsync($"/v1/admin/products/{freshId}/offers", Offer(ends: "2026-09-01T00:00:00"))).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await manager.PostAsync($"/v1/admin/products/{freshId}/offers", Offer(zakat: true))).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await manager.PostAsync($"/v1/admin/products/{freshId}/offers", Offer())).Response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    [Rule("RUL-07")]
    public async Task A_cause_with_units_needs_a_label_and_there_is_one_general_fund()
    {
        var manager = await ManagerAsync();
        var (_, p) = await manager.PostAsync("/v1/admin/products", Product("cause", "t-cause-block"));
        var id = p.GetProperty("id").GetGuid();
        object Cause(string? unit, string? label, bool generalFund = false) => new
        {
            unitAmount = unit,
            unitLabelAr = label,
            targetAmount = "10000.00",
            allowCustomAmount = true,
            minAmount = "10.00",
            zakatEligible = false,
            closeOnTarget = false,
            isGeneralFund = generalFund,
            endsAtLocal = generalFund ? null : "2027-01-31T23:59:00",
        };

        (await manager.SendAsync(HttpMethod.Put, $"/v1/admin/products/{id}/cause", Cause("100.00", null))).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await manager.SendAsync(HttpMethod.Put, $"/v1/admin/products/{id}/cause", Cause(null, null, generalFund: true))).Body.Code().ShouldBe("cause.general_fund_exists");
        (await manager.SendAsync(HttpMethod.Put, $"/v1/admin/products/{id}/cause", Cause("100.00", "حقيبة"))).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Rule("RUL-09")]
    public async Task After_the_end_date_a_campaign_is_flagged_ended_and_refuses_new_lines()
    {
        var cause = await factory.ProductIdAsync("water-for-schools");
        await factory.ExecuteSqlAsync($"UPDATE store.cause_detail SET ends_at = now() - interval '1 minute' WHERE product_id = '{cause}'");
        try
        {
            var (_, p) = await FreshAsync("/v1/catalog/products/water-for-schools");
            p.GetProperty("cause").GetProperty("isOpen").GetBoolean().ShouldBeFalse();
            (await (await factory.CustomerAsync()).PostAsync("/v1/cart/lines", Shop.Donate(cause))).Body.Code().ShouldBe("cart.campaign_ended");
        }
        finally
        {
            await factory.ExecuteSqlAsync($"UPDATE store.cause_detail SET ends_at = now() + interval '90 days' WHERE product_id = '{cause}'");
        }
    }

    [Fact]
    [Rule("RUL-10")]
    [Rule("RUL-55")]
    public async Task Reaching_the_target_closes_only_with_close_on_target_and_money_keeps_coming_until_then()
    {
        var cause = await factory.ProductIdAsync("learning-data");
        await factory.ExecuteSqlAsync($"UPDATE store.goal_progress SET seed_raised = 60000 WHERE product_id = '{cause}'");   // target 60000.00 reached
        try
        {
            var (_, open) = await FreshAsync("/v1/catalog/products/learning-data");
            open.GetProperty("cause").GetProperty("percent").GetInt32().ShouldBeGreaterThanOrEqualTo(100);
            open.GetProperty("cause").GetProperty("isOpen").GetBoolean().ShouldBeTrue();
            await factory.PaidOrderAsync(await factory.CustomerAsync(), Shop.Donate(cause));                              // over-target gift accepted

            await factory.ExecuteSqlAsync($"UPDATE store.cause_detail SET close_on_target = true WHERE product_id = '{cause}'");
            var (_, closed) = await FreshAsync("/v1/catalog/products/learning-data");
            closed.GetProperty("cause").GetProperty("isOpen").GetBoolean().ShouldBeFalse();
            (await (await factory.CustomerAsync()).PostAsync("/v1/cart/lines", Shop.Donate(cause))).Response.IsSuccessStatusCode.ShouldBeFalse();
        }
        finally
        {
            await factory.ExecuteSqlAsync($"UPDATE store.cause_detail SET close_on_target = false WHERE product_id = '{cause}'");
            await factory.ExecuteSqlAsync($"UPDATE store.goal_progress SET seed_raised = 0 WHERE product_id = '{cause}'");
        }
    }

    [Fact]
    [Rule("RUL-44")]
    public async Task Sponsorship_may_exceed_the_target_units()
    {
        var product = await factory.ProductIdAsync("letters-tales");
        await factory.ExecuteSqlAsync($"UPDATE store.sponsorship_offer SET target_units = 1 WHERE product_id = '{product}' AND is_active");

        var order = await factory.PaidOrderAsync(await factory.CustomerAsync(), Shop.SponsorUnits(product, 3));

        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.sponsored_unit u JOIN store.order_line l ON l.id = u.order_line_id WHERE l.order_id = '{order.OrderId}'")).ShouldBe(3);
        var (_, p) = await FreshAsync("/v1/catalog/products/letters-tales");
        p.GetProperty("sponsorship").GetProperty("isOpen").GetBoolean().ShouldBeTrue();
        p.GetProperty("sponsorship").GetProperty("percent").GetInt32().ShouldBeGreaterThan(100);
    }

    [Fact]
    [Rule("RUL-23")]
    public async Task The_gift_feed_hides_anonymous_givers()
    {
        var customer = await factory.CustomerAsync();
        await customer.PostAsync("/v1/cart/lines", Shop.Donate(await factory.ProductIdAsync("tablet-for-student")));
        var (_, quote) = await customer.PostAsync("/v1/checkout/quote");
        var (_, order) = await customer.PostAsync("/v1/checkout", new
        {
            contact = new { fullName = "خالد سعد الحربي", email = "k@example.com" },
            isAnonymous = true,
            saveAddress = false,
            paymentMethod = "mada",
            acceptTerms = true,
            expectedGrandTotal = quote.GetProperty("grandTotal").GetString(),
            locale = "ar",
        }, new Dictionary<string, string> { ["Idempotency-Key"] = Guid.NewGuid().ToString() });
        await factory.SettleAsync(order.GetProperty("reference").GetString()!);

        var (_, feed) = await FreshAsync("/v1/catalog/products/tablet-for-student/feed");
        var text = feed.ToString();
        text.ShouldContain("فاعل خير");
        text.ShouldNotContain("خالد");
    }
}
