using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.Scenarios;

/// T-5 — checkout rules (plan 14 S-15: RUL-24 matrix, RUL-25 … RUL-28, RUL-30 race, RUL-31).
public sealed partial class CheckoutRulesTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    private static object Body(string total, object? address = null, string? name = "عميل تجربة", bool terms = true) => new
    {
        contact = new { fullName = name, email = "c@example.com" },
        isAnonymous = false,
        shippingAddress = address,
        saveAddress = false,
        paymentMethod = "mada",
        acceptTerms = terms,
        expectedGrandTotal = total,
        locale = "ar",
    };

    private static Dictionary<string, string> Key() => new() { ["Idempotency-Key"] = Guid.NewGuid().ToString() };

    private static async Task<string> QuoteTotalAsync(ActorClient c) => (await c.PostAsync("/v1/checkout/quote")).Body.GetProperty("grandTotal").GetString()!;

    [Fact]
    [Rule("RUL-24")]
    public async Task Checkout_revalidates_every_line_and_charges_nothing_while_one_fails()
    {
        var customer = await factory.CustomerAsync();
        var cause = await factory.ProductIdAsync("learning-data");
        await customer.PostAsync("/v1/cart/lines", Shop.Donate(cause));
        await customer.PostAsync("/v1/cart/lines", Shop.Buy(await factory.ProductIdAsync("qudrat-prep")));
        var total = await QuoteTotalAsync(customer);
        await factory.ExecuteSqlAsync($"UPDATE store.cause_detail SET ends_at = now() - interval '1 day' WHERE product_id = '{cause}'");
        try
        {
            var (quoted, quote) = await customer.PostAsync("/v1/checkout/quote");
            quoted.StatusCode.ShouldBe(HttpStatusCode.OK);
            quote.GetProperty("valid").GetBoolean().ShouldBeFalse();
            quote.GetProperty("lines").EnumerateArray().Count(l => l.GetProperty("status").GetString() == "invalid").ShouldBe(1);

            var (placed, problem) = await customer.PostAsync("/v1/checkout", Body(total), Key());
            placed.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            problem.Code().ShouldBe("checkout.lines_invalid");
            problem.GetProperty("lines").GetArrayLength().ShouldBe(1);
            (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.orders WHERE customer_id = '{customer.Id}'")).ShouldBe(0);
        }
        finally
        {
            await factory.ExecuteSqlAsync($"UPDATE store.cause_detail SET ends_at = now() + interval '60 days' WHERE product_id = '{cause}'");
        }
    }

    [Fact]
    [Rule("RUL-25")]
    public async Task Checkout_needs_a_signed_in_customer_a_name_and_accepted_terms()
    {
        var customer = await factory.CustomerAsync();
        await customer.PostAsync("/v1/cart/lines", Shop.Donate(await factory.ProductIdAsync("school-bag")));
        var total = await QuoteTotalAsync(customer);

        (await factory.Anonymous().SendJsonAsync(HttpMethod.Post, "/v1/checkout", Body(total))).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await customer.PostAsync("/v1/checkout", Body(total, name: null), Key())).Body.Code().ShouldBe("checkout.contact_required");
        (await customer.PostAsync("/v1/checkout", Body(total, terms: false), Key())).Body.Code().ShouldBe("checkout.terms_not_accepted");
        (await customer.PostAsync("/v1/checkout", Body(total), Key())).Response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await factory.ScalarAsync<string>($"SELECT full_name FROM store.customer WHERE id = '{customer.Id}'")).ShouldBe("عميل تجربة");
    }

    [Fact]
    [Rule("RUL-26")]
    public async Task Shipping_only_for_print_lines_as_a_flat_fee()
    {
        var gifts = await factory.CustomerAsync();
        await gifts.PostAsync("/v1/cart/lines", Shop.Donate(await factory.ProductIdAsync("school-bag")));
        await gifts.PostAsync("/v1/cart/lines", Shop.Buy(await factory.ProductIdAsync("formative-guide"), 1, await factory.FormatIdAsync("formative-guide", "digital")));
        var (_, noShip) = await gifts.PostAsync("/v1/checkout/quote");
        noShip.GetProperty("shippingTotal").GetString().ShouldBe("0.00");
        noShip.GetProperty("requiresShipping").GetBoolean().ShouldBeFalse();

        var buyer = await factory.CustomerAsync();
        await buyer.PostAsync("/v1/cart/lines", Shop.Buy(await factory.ProductIdAsync("formative-guide"), 3, await factory.FormatIdAsync("formative-guide", "print")));
        var (_, ship) = await buyer.PostAsync("/v1/checkout/quote");
        ship.GetProperty("shippingTotal").GetString().ShouldBe("25.00");                          // once per order, not per copy
        (await buyer.PostAsync("/v1/checkout", Body(ship.GetProperty("grandTotal").GetString()!), Key())).Body.Code().ShouldBe("checkout.address_required");
    }

    [Fact]
    [Rule("RUL-27")]
    public async Task VAT_is_included_in_purchase_prices_when_enabled_and_zero_on_gifts()
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        var customer = await factory.CustomerAsync();
        var course = await factory.ProductAsync("digital-teacher");
        var price = D(course.GetProperty("course").GetProperty("price").GetString()!);
        await customer.PostAsync("/v1/cart/lines", Shop.Buy(course.GetProperty("id").GetGuid()));
        await customer.PostAsync("/v1/cart/lines", Shop.Donate(await factory.ProductIdAsync("school-bag")));

        (await customer.PostAsync("/v1/checkout/quote")).Body.GetProperty("vatTotal").GetString().ShouldBe("0.00");
        (await admin.SendAsync(HttpMethod.Put, "/v1/admin/settings/vat_enabled", new { value = true })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        try
        {
            var (_, quote) = await customer.PostAsync("/v1/checkout/quote");
            var expected = decimal.Round(price * 15m / 115m, 2, MidpointRounding.AwayFromZero);             // 350.00 → 45.65
            D(quote.GetProperty("vatTotal").GetString()!).ShouldBe(expected);
            quote.GetProperty("lines").EnumerateArray().Select(l => D(l.GetProperty("vatAmount").GetString()!)).Order().ShouldBe([0m, expected]);
        }
        finally
        {
            (await admin.SendAsync(HttpMethod.Put, "/v1/admin/settings/vat_enabled", new { value = false })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    [Rule("RUL-28")]
    [Rule("RUL-31")]
    public async Task The_reference_is_T3_YY_NNNNNN_and_the_grand_total_adds_up()
    {
        var customer = await factory.CustomerAsync();
        await customer.PostAsync("/v1/cart/lines", Shop.Buy(await factory.ProductIdAsync("letters-tales"), 2, await factory.FormatIdAsync("letters-tales", "print")));
        await customer.PostAsync("/v1/cart/lines", Shop.SponsorAmount(await factory.ProductIdAsync("smart-study"), "40.00"));
        var (_, quote) = await customer.PostAsync("/v1/checkout/quote");

        var (placed, order) = await customer.PostAsync("/v1/checkout", Body(quote.GetProperty("grandTotal").GetString()!, Shop.ShippingAddress), Key());

        placed.StatusCode.ShouldBe(HttpStatusCode.Created);
        var year = (factory.Clock.UtcNow.Year % 100).ToString("D2", CultureInfo.InvariantCulture);
        order.GetProperty("reference").GetString()!.ShouldMatch($"^T3-{year}-\\d{{6}}$");
        D(quote.GetProperty("grandTotal").GetString()!).ShouldBe(
            D(quote.GetProperty("purchasesTotal").GetString()!) + D(quote.GetProperty("shippingTotal").GetString()!) + D(quote.GetProperty("giftsTotal").GetString()!));
        MoneyWire().IsMatch(order.GetProperty("grandTotal").GetString()!).ShouldBeTrue();          // a "0.00" string, never a float
    }

    [Fact]
    [Rule("RUL-30")]
    public async Task Two_customers_racing_for_the_last_copy_one_wins()
    {
        var book = await factory.ProductIdAsync("study-planner");
        var print = await factory.FormatIdAsync("study-planner", "print");
        var a = await factory.CustomerAsync();
        var b = await factory.CustomerAsync();
        await a.PostAsync("/v1/cart/lines", Shop.Buy(book, 1, print));
        await b.PostAsync("/v1/cart/lines", Shop.Buy(book, 1, print));
        var total = await QuoteTotalAsync(a);
        var onHand = await factory.ScalarAsync<int>($"SELECT stock_on_hand FROM store.book_format WHERE id = '{print}'");
        await factory.ExecuteSqlAsync($"UPDATE store.book_format SET stock_on_hand = stock_reserved + 1 WHERE id = '{print}'");
        try
        {
            var results = await Task.WhenAll(
                a.PostAsync("/v1/checkout", Body(total, Shop.ShippingAddress), Key()),
                b.PostAsync("/v1/checkout", Body(total, Shop.ShippingAddress), Key()));

            results.Count(r => r.Response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
            // The loser either loses the reservation (RUL-30, 409) or, when the winner committed first, fails re-validation
            // because stock is no longer available (RUL-24, 422 with the line's stock reason).
            var loser = results.Single(r => r.Response.StatusCode != HttpStatusCode.Created).Body;
            loser.Code().ShouldBeOneOf("checkout.out_of_stock", "checkout.lines_invalid");
            if (loser.Code() == "checkout.lines_invalid")
            {
                loser.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("code").GetString()).ShouldContain("cart.out_of_stock");
            }

            (await factory.ScalarAsync<bool>($"SELECT stock_reserved <= stock_on_hand FROM store.book_format WHERE id = '{print}'")).ShouldBeTrue();
        }
        finally
        {
            await factory.ExecuteSqlAsync($"UPDATE store.book_format SET stock_on_hand = {onHand} WHERE id = '{print}'");
        }
    }

    [Fact]
    [Rule("RUL-13")]
    public async Task A_course_in_an_unpaid_order_cannot_be_added_again()
    {
        var customer = await factory.CustomerAsync();
        var course = await factory.ProductIdAsync("python-kids");
        var order = await customer.OrderAsync(Shop.Buy(course));

        var (added, problem) = await customer.PostAsync("/v1/cart/lines", Shop.Buy(course));

        added.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        problem.Code().ShouldBe("cart.course_in_unpaid_order");
        problem.GetProperty("orderRef").GetString().ShouldBe(order.Reference);
    }

    [Fact]
    public async Task The_same_Idempotency_Key_replays_and_a_different_body_is_refused()
    {
        var customer = await factory.CustomerAsync();
        await customer.PostAsync("/v1/cart/lines", Shop.Donate(await factory.ProductIdAsync("water-for-schools")));
        var total = await QuoteTotalAsync(customer);
        var key = Key();

        var (first, a) = await customer.PostAsync("/v1/checkout", Body(total), key);
        var (second, b) = await customer.PostAsync("/v1/checkout", Body(total), key);
        var (third, c) = await customer.PostAsync("/v1/checkout", Body(total, name: "آخر"), key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.Headers.GetValues("Idempotent-Replayed").Single().ShouldBe("true");
        b.GetProperty("reference").GetString().ShouldBe(a.GetProperty("reference").GetString());
        third.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        c.Code().ShouldBe("idempotency.key_reused");
        (await customer.PostAsync("/v1/checkout", Body(total))).Body.Code().ShouldBe("idempotency.key_required");
    }

    [GeneratedRegex(@"^\d+\.\d{2}$")]
    private static partial Regex MoneyWire();
}
