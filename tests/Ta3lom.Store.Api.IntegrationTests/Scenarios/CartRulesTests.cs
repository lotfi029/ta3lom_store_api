using System.Net;
using System.Text.Json;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Features.Cart.Commands.DeleteExpiredGuestCarts;

namespace Ta3lom.Store.Api.IntegrationTests.Scenarios;

/// T-5 — cart rules (plan 14 S-13: one test per RUL-11 … RUL-19 and RUL-21).
public sealed class CartRulesTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private static Task<(HttpStatusCode Status, JsonElement Body)> AddAsync(HttpClient client, object line) =>
        client.SendJsonAsync(HttpMethod.Post, "/v1/cart/lines", line);

    private static async Task<JsonElement> CartAsync(HttpClient client) => (await client.SendJsonAsync(HttpMethod.Get, "/v1/cart")).Body;

    private static IEnumerable<JsonElement> Lines(JsonElement cart) =>
        cart.GetProperty("purchases").EnumerateArray().Concat(cart.GetProperty("gifts").EnumerateArray());

    [Fact]
    [Rule("RUL-11")]
    public async Task A_guest_gets_a_server_cart_behind_an_opaque_HttpOnly_cookie()
    {
        var guest = factory.Browser();
        var course = await factory.ProductIdAsync("python-kids");

        var response = await guest.PostAsync("/v1/cart/lines", System.Net.Http.Json.JsonContent.Create(Shop.Buy(course)), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("t3s_cart=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldNotContain(course.ToString());
        var cart = await CartAsync(guest);
        cart.GetProperty("isGuest").GetBoolean().ShouldBeTrue();
        Lines(cart).Single().GetProperty("productId").GetGuid().ShouldBe(course);
        (await CartAsync(factory.Browser())).GetProperty("purchases").GetArrayLength().ShouldBe(0);   // another visitor
    }

    [Fact]
    [Rule("RUL-12")]
    public async Task Line_kinds_and_modes_follow_the_matrix()
    {
        var c = factory.Browser();
        var book = await factory.ProductIdAsync("science-stars-book");
        var print = await factory.FormatIdAsync("science-stars-book", "print");
        var digital = await factory.FormatIdAsync("science-stars-book", "digital");
        var cause = await factory.ProductIdAsync("water-for-schools");
        var fund = await factory.ProductIdAsync("education-fund");
        var offer = await factory.ProductIdAsync("digital-teacher");

        (await AddAsync(c, Shop.Buy(book, 21, print))).Body.Code().ShouldBe("cart.quantity_limit");
        (await AddAsync(c, new { productId = cause, kind = "buy", mode = "units", quantity = 1 })).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await AddAsync(c, new { productId = fund, kind = "donate", mode = "units", quantity = 1, intent = "sadaqa" })).Body.Code().ShouldBe("cart.mode_not_allowed");
        (await AddAsync(c, Shop.SponsorAmount(offer, "5.00"))).Body.Code().ShouldBe("cart.amount_below_minimum");
        (await AddAsync(c, Shop.Buy(book, 0, print))).Body.Code().ShouldBe("validation.failed");

        (await AddAsync(c, Shop.Buy(book, 5, digital))).Status.ShouldBe(HttpStatusCode.Created);
        Lines(await CartAsync(c)).Single(l => l.GetProperty("bookFormat").ValueKind == JsonValueKind.Object).GetProperty("quantity").GetInt32().ShouldBe(1);
        (await AddAsync(c, Shop.SponsorUnits(offer, 3))).Status.ShouldBe(HttpStatusCode.Created);
        (await AddAsync(c, Shop.Donate(cause, 2))).Status.ShouldBe(HttpStatusCode.Created);
        var sponsor = Lines(await CartAsync(c)).Single(l => l.GetProperty("kind").GetString() == "sponsor");
        sponsor.GetProperty("amount").GetString().ShouldBe("1050.00");                 // 3 × 350.00 seats
    }

    [Fact]
    [Rule("RUL-13")]
    public async Task The_same_course_cannot_be_added_twice()
    {
        var c = factory.Browser();
        var course = await factory.ProductIdAsync("family-remote");
        (await AddAsync(c, Shop.Buy(course))).Status.ShouldBe(HttpStatusCode.Created);

        var (status, problem) = await AddAsync(c, Shop.Buy(course));

        status.ShouldBe(HttpStatusCode.Conflict);
        problem.Code().ShouldBe("cart.duplicate_course");
        (await AddAsync(c, Shop.SponsorUnits(course, 1))).Status.ShouldBe(HttpStatusCode.Created);   // sponsoring is always allowed
    }

    [Fact]
    [Rule("RUL-14")]
    public async Task Identical_unit_lines_merge_amount_and_dedicated_lines_never_do()
    {
        var c = factory.Browser();
        var book = await factory.ProductIdAsync("letters-tales");
        var print = await factory.FormatIdAsync("letters-tales", "print");
        var offer = await factory.ProductIdAsync("smart-study");
        var cause = await factory.ProductIdAsync("school-bag");
        var dedication = new { toName = "أم محمد", toMobile = "0551112233", templateKey = "hasanat" };

        await AddAsync(c, Shop.Buy(book, 2, print));
        await AddAsync(c, Shop.Buy(book, 1, print));
        await AddAsync(c, Shop.SponsorAmount(offer, "50.00"));
        await AddAsync(c, Shop.SponsorAmount(offer, "50.00"));
        await AddAsync(c, new { productId = cause, kind = "donate", mode = "units", quantity = 1, intent = "sadaqa", dedication });
        await AddAsync(c, new { productId = cause, kind = "donate", mode = "units", quantity = 1, intent = "sadaqa", dedication });

        var lines = Lines(await CartAsync(c)).ToList();
        lines.Where(l => l.GetProperty("kind").GetString() == "buy").Select(l => l.GetProperty("quantity").GetInt32()).ShouldBe([3]);
        lines.Count(l => l.GetProperty("mode").GetString() == "amount").ShouldBe(2);
        lines.Count(l => l.GetProperty("kind").GetString() == "donate").ShouldBe(2);
    }

    [Fact]
    [Rule("RUL-15")]
    public async Task Checkout_reprices_from_the_catalogue()
    {
        var customer = await factory.CustomerAsync();
        var book = await factory.ProductIdAsync("formative-guide");
        var print = await factory.FormatIdAsync("formative-guide", "print");
        (await customer.PostAsync("/v1/cart/lines", Shop.Buy(book, 1, print))).Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var (_, before) = await customer.PostAsync("/v1/checkout/quote", new { });
        await factory.ExecuteSqlAsync($"UPDATE store.book_format SET price = price + 10 WHERE id = '{print}'");
        try
        {
            var (_, after) = await customer.PostAsync("/v1/checkout/quote", new { });
            (decimal.Parse(after.GetProperty("purchasesTotal").GetString()!, System.Globalization.CultureInfo.InvariantCulture)
             - decimal.Parse(before.GetProperty("purchasesTotal").GetString()!, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(10m);
            var (placed, problem) = await customer.PostAsync("/v1/checkout", new
            {
                contact = new { fullName = "عميل", email = "c@example.com" },
                isAnonymous = false,
                shippingAddress = Shop.ShippingAddress,
                saveAddress = false,
                paymentMethod = "mada",
                acceptTerms = true,
                expectedGrandTotal = before.GetProperty("grandTotal").GetString(),
                locale = "ar",
            }, new Dictionary<string, string> { ["Idempotency-Key"] = Guid.NewGuid().ToString() });
            placed.IsSuccessStatusCode.ShouldBeFalse(problem.ToString());                // the old total is refused: the customer sees the new one
        }
        finally
        {
            await factory.ExecuteSqlAsync($"UPDATE store.book_format SET price = price - 10 WHERE id = '{print}'");
        }
    }

    [Fact]
    [Rule("RUL-16")]
    public async Task Guest_carts_expire_after_30_days_customer_carts_do_not()
    {
        var guest = factory.Browser();
        await AddAsync(guest, Shop.Donate(await factory.ProductIdAsync("learning-data")));
        var customer = await factory.CustomerAsync();
        await customer.PostAsync("/v1/cart/lines", Shop.Donate(await factory.ProductIdAsync("learning-data")));
        var guestCart = (await CartAsync(guest)).GetProperty("id").GetGuid();

        var start = factory.Clock.UtcNow;
        try
        {
            factory.Clock.Advance(TimeSpan.FromDays(31));
            (await factory.RunAsync<DeleteExpiredGuestCartsCommand, int>(new DeleteExpiredGuestCartsCommand())).Value.ShouldBeGreaterThanOrEqualTo(1);
        }
        finally
        {
            factory.Clock.Set(start);
        }

        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.cart WHERE id = '{guestCart}'")).ShouldBe(0);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.cart WHERE customer_id = '{customer.Id}'")).ShouldBe(1);
    }

    [Fact]
    [Rule("RUL-17")]
    public async Task Sign_in_merges_the_guest_cart_capping_quantities_and_dropping_owned_courses()
    {
        var mobile = ScenarioFactory.NewMobile();
        var owner = await factory.CustomerAsync(mobile);
        var course = await factory.ProductIdAsync("digital-teacher");
        await factory.PaidOrderAsync(owner, Shop.Buy(course));
        var book = await factory.ProductIdAsync("study-planner");
        var print = await factory.FormatIdAsync("study-planner", "print");
        await owner.PostAsync("/v1/cart/lines", Shop.Buy(book, 15, print));

        var browser = factory.Browser();
        await AddAsync(browser, Shop.Buy(book, 7, print));
        await AddAsync(browser, Shop.Buy(course));

        var session = await factory.SignInAsync(browser, mobile);

        session.GetProperty("cartNotices").EnumerateArray().Select(n => n.GetProperty("code").GetString())
            .ShouldBe(["cart.merge_combined", "cart.merge_dropped_owned_course"], ignoreOrder: true);
        var lines = Lines(await CartAsync(browser)).ToList();
        lines.Single(l => l.GetProperty("kind").GetString() == "buy").GetProperty("quantity").GetInt32().ShouldBe(20);
        lines.ShouldNotContain(l => l.GetProperty("productId").GetGuid() == course);
    }

    [Fact]
    [Rule("RUL-18")]
    public async Task A_cart_holds_at_most_50_lines_and_100000_SAR()
    {
        var c = factory.Browser();
        var offer = await factory.ProductIdAsync("early-childhood");
        for (var i = 0; i < 50; i++)
        {
            (await AddAsync(c, Shop.SponsorAmount(offer, "10.00"))).Status.ShouldBe(HttpStatusCode.Created);
        }

        (await AddAsync(c, Shop.SponsorAmount(offer, "10.00"))).Body.Code().ShouldBe("cart.too_many_lines");
        (await AddAsync(factory.Browser(), Shop.SponsorUnits(await factory.ProductIdAsync("python-kids"), 999))).Body.Code().ShouldBe("cart.total_exceeded");
    }

    [Fact]
    [Rule("RUL-19")]
    public async Task Zakat_only_on_zakat_eligible_causes_and_buy_lines_have_no_intent()
    {
        var c = factory.Browser();

        (await AddAsync(c, Shop.Donate(await factory.ProductIdAsync("water-for-schools"), 1, "zakat"))).Body.Code().ShouldBe("cart.zakat_not_allowed");
        (await AddAsync(c, Shop.Donate(await factory.ProductIdAsync("tablet-for-student"), 1, "zakat"))).Status.ShouldBe(HttpStatusCode.Created);
        (await AddAsync(c, Shop.Buy(await factory.ProductIdAsync("qudrat-prep")))).Status.ShouldBe(HttpStatusCode.Created);

        var lines = Lines(await CartAsync(c)).ToList();
        lines.Single(l => l.GetProperty("kind").GetString() == "donate").GetProperty("intent").GetString().ShouldBe("zakat");
        lines.Single(l => l.GetProperty("kind").GetString() == "buy").GetProperty("intent").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    [Rule("RUL-21")]
    public async Task A_dedication_is_validated_stored_in_E164_and_shown_masked()
    {
        var c = factory.Browser();
        var cause = await factory.ProductIdAsync("tablet-for-student");
        object Line(object dedication) => new { productId = cause, kind = "donate", mode = "units", quantity = 1, intent = "sadaqa", dedication };

        (await AddAsync(c, Line(new { toName = "أم محمد", toMobile = "123", templateKey = "hasanat" }))).Body.Code().ShouldBe("cart.dedication_invalid");
        (await AddAsync(c, Line(new { toName = "أم محمد", toMobile = "0551112233", text = "نص حر" }))).Body.Code().ShouldBe("cart.dedication_free_text_disabled");
        (await AddAsync(c, new
        {
            productId = await factory.ProductIdAsync("smart-study"),
            kind = "buy",
            mode = "units",
            quantity = 1,
            dedication = new { toName = "أم محمد", toMobile = "0551112233", templateKey = "hasanat" }
        })).Body.Code().ShouldBe("cart.dedication_not_allowed");
        (await AddAsync(c, Line(new { toName = "أم محمد", toMobile = "0551112233", fromName = "سارة", templateKey = "hasanat" }))).Status.ShouldBe(HttpStatusCode.Created);

        var line = Lines(await CartAsync(c)).Single();
        line.GetProperty("dedication").GetProperty("toMobile").GetString().ShouldBe("05•••••233");
        (await factory.ScalarAsync<string>($"SELECT dedication::text FROM store.cart_line WHERE id = '{line.GetProperty("id").GetGuid()}'")).ShouldContain("+966551112233");
    }
}
