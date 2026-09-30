using System.Net;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Application.Features.Orders.Commands.ExpireUnpaidOrders;

namespace Ta3lom.Store.Api.IntegrationTests.Scenarios;

/// T-2 — settlement (plan 14 S-16 tests; doc 15 §2): `IOrderSettlement` through the dev-trigger command.
public sealed class SettlementTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private Task<long> CountAsync(string sql) => factory.ScalarAsync<long>(sql);

    private static string OrderId(PlacedOrder o) => $"'{o.OrderId}'";

    [Fact]
    [Rule("RUL-39")]
    public async Task The_same_confirmation_five_times_changes_the_order_once()
    {
        var customer = await factory.CustomerAsync();
        var order = await customer.OrderAsync(
            Shop.Buy(await factory.ProductIdAsync("smart-study")),
            Shop.Buy(await factory.ProductIdAsync("science-stars-book"), 1, await factory.FormatIdAsync("science-stars-book", "print")));

        var outcomes = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            outcomes.Add((await factory.SettleAsync(order.Reference, confirmationId: "moyasar:evt_same")).Outcome);
        }

        outcomes.ShouldBe(["paid", "already_paid", "already_paid", "already_paid", "already_paid"]);
        (await CountAsync($"SELECT count(*) FROM store.enrolment e JOIN store.order_line l ON l.id = e.order_line_id WHERE l.order_id = {OrderId(order)}")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM store.invoice WHERE order_id = {OrderId(order)}")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM store.stock_movement WHERE reason = 'sale' AND ref_id = '{order.Reference}'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM store.outbox_message WHERE type = 'OrderPaid' AND payload->>'orderId' = '{order.OrderId}'")).ShouldBe(1);
    }

    [Fact]
    [Rule("RUL-39")]
    public async Task Two_confirmations_in_parallel_settle_once()
    {
        var customer = await factory.CustomerAsync();
        var order = await customer.OrderAsync(Shop.Buy(await factory.ProductIdAsync("smart-study")));

        var results = await Task.WhenAll(
            factory.SettleAsync(order.Reference, confirmationId: "moyasar:evt_a"),
            factory.SettleAsync(order.Reference, confirmationId: "moyasar:evt_b"));

        results.Select(r => r.Outcome).Order().ShouldBe(["already_paid", "paid"]);
        (await CountAsync($"SELECT count(*) FROM store.invoice WHERE order_id = {OrderId(order)}")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM store.enrolment e JOIN store.order_line l ON l.id = e.order_line_id WHERE l.order_id = {OrderId(order)}")).ShouldBe(1);
    }

    [Fact]
    [Rule("RUL-35")]
    public async Task A_wrong_amount_parks_the_order_in_payment_review_without_effects()
    {
        var customer = await factory.CustomerAsync();
        var course = await factory.ProductIdAsync("digital-teacher");
        var raisedBefore = await factory.ScalarAsync<decimal>($"SELECT raised_amount FROM store.goal_progress WHERE product_id = '{course}'");
        var order = await customer.OrderAsync(Shop.Buy(course), Shop.SponsorUnits(course, 1));

        var result = await factory.SettleAsync(order.Reference, amount: "1.00");

        result.Outcome.ShouldBe("payment_review");
        result.Status.ShouldBe("payment_review");
        (await CountAsync($"SELECT count(*) FROM store.invoice WHERE order_id = {OrderId(order)}")).ShouldBe(0);
        (await CountAsync($"SELECT count(*) FROM store.sponsored_unit u JOIN store.order_line l ON l.id = u.order_line_id WHERE l.order_id = {OrderId(order)}")).ShouldBe(0);
        (await factory.ScalarAsync<decimal>($"SELECT raised_amount FROM store.goal_progress WHERE product_id = '{course}'")).ShouldBe(raisedBefore);
    }

    [Fact]
    [Rule("RUL-29")]
    [Rule("RUL-30")]
    [Rule("RUL-35")]
    public async Task Expiry_releases_stock_and_restores_the_cart_and_a_late_confirmation_goes_to_review()
    {
        var customer = await factory.CustomerAsync();
        var book = await factory.ProductIdAsync("science-stars-book");
        var print = await factory.FormatIdAsync("science-stars-book", "print");
        var reservedBefore = await factory.ScalarAsync<int>($"SELECT stock_reserved FROM store.book_format WHERE id = '{print}'");
        var order = await customer.OrderAsync(Shop.Buy(book, 2, print));
        (await factory.ScalarAsync<int>($"SELECT stock_reserved FROM store.book_format WHERE id = '{print}'")).ShouldBe(reservedBefore + 2);

        var start = factory.Clock.UtcNow;
        try
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(61));
            (await factory.RunAsync<ExpireUnpaidOrdersCommand, int>(new ExpireUnpaidOrdersCommand())).Value.ShouldBeGreaterThanOrEqualTo(1);
        }
        finally
        {
            factory.Clock.Set(start);
        }

        (await factory.ScalarAsync<string>($"SELECT status FROM store.orders WHERE id = {OrderId(order)}")).ShouldBe("expired");
        (await factory.ScalarAsync<int>($"SELECT stock_reserved FROM store.book_format WHERE id = '{print}'")).ShouldBe(reservedBefore);
        var (cartResponse, cart) = await customer.GetAsync("/v1/cart");
        cartResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        cart.GetProperty("purchases").EnumerateArray().ShouldContain(l => l.GetProperty("quantity").GetInt32() == 2);

        (await factory.SettleAsync(order.Reference)).Status.ShouldBe("payment_review");
    }

    [Fact]
    [Rule("RUL-13")]
    public async Task A_duplicate_course_paid_in_a_race_creates_no_second_enrolment_and_alerts_finance()
    {
        var customer = await factory.CustomerAsync();
        var course = await factory.ProductIdAsync("early-childhood");
        var first = await customer.OrderAsync(Shop.Buy(course));
        // The race: the first order leaves "unpaid" for a moment, so checkout accepts the same course again.
        await factory.ExecuteSqlAsync($"UPDATE store.orders SET status = 'expired' WHERE id = {OrderId(first)}");
        var second = await customer.OrderAsync(Shop.Buy(course));
        await factory.ExecuteSqlAsync($"UPDATE store.orders SET status = 'pending_payment' WHERE id = {OrderId(first)}");

        await factory.SettleAsync(first.Reference);
        await factory.SettleAsync(second.Reference);

        (await CountAsync($"SELECT count(*) FROM store.enrolment WHERE customer_id = '{customer.Id}' AND product_id = '{course}'")).ShouldBe(1);
        (await factory.ScalarAsync<string>($"SELECT fulfilment_status FROM store.order_line WHERE order_id = {OrderId(second)}")).ShouldBe("pending");
        (await CountAsync($"SELECT count(*) FROM store.outbox_message WHERE type = 'FinanceAlert' AND payload->>'kind' = 'duplicate_course' AND payload->>'reference' = '{second.Reference}'")).ShouldBe(1);
    }

    [Fact]
    [Rule("RUL-20")]
    public async Task A_zakat_donation_is_recorded_in_the_zakat_ledger()
    {
        var customer = await factory.CustomerAsync();
        var cause = await factory.ProductIdAsync("school-bag");

        var order = await factory.PaidOrderAsync(customer, Shop.Donate(cause, 2, "zakat"));

        var line = await factory.ScalarAsync<Guid>($"SELECT id FROM store.order_line WHERE order_id = {OrderId(order)}");
        (await factory.ScalarAsync<decimal>($"SELECT sum(amount) FROM store.zakat_ledger WHERE direction = 'in' AND order_line_id = '{line}'")).ShouldBe(360.00m);
        (await factory.ScalarAsync<string>($"SELECT fulfilment_status FROM store.order_line WHERE id = '{line}'")).ShouldBe("received");
    }

    [Fact]
    [Rule("RUL-39")]
    public async Task Goal_counters_change_once_per_product_per_order()
    {
        var customer = await factory.CustomerAsync();
        var product = await factory.ProductIdAsync("family-remote");
        const string Goal = "SELECT raised_amount::text || '|' || donor_count || '|' || funded_units FROM store.goal_progress WHERE product_id = '{0}'";
        var before = (await factory.ScalarAsync<string>(string.Format(System.Globalization.CultureInfo.InvariantCulture, Goal, product))).Split('|');

        await factory.PaidOrderAsync(customer, Shop.SponsorUnits(product, 2), Shop.SponsorAmount(product, "30.00"));

        var after = (await factory.ScalarAsync<string>(string.Format(System.Globalization.CultureInfo.InvariantCulture, Goal, product))).Split('|');
        (decimal.Parse(after[0], System.Globalization.CultureInfo.InvariantCulture) - decimal.Parse(before[0], System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(210.00m);
        (int.Parse(after[1], System.Globalization.CultureInfo.InvariantCulture) - int.Parse(before[1], System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(1);
        (int.Parse(after[2], System.Globalization.CultureInfo.InvariantCulture) - int.Parse(before[2], System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(2);
        (await factory.IntegrityFailuresAsync()).ShouldBeEmpty();
    }
}
