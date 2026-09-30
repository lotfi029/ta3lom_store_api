using System.Net;
using System.Text.Json;

namespace Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;

/// Builders over the public API (T-1): catalogue lookups, cart lines and checkout, the way the storefront does it.
public static class Shop
{
    public static readonly object ShippingAddress = new
    {
        recipientName = "مستلم تجربة",
        recipientMobile = "0551234567",
        city = "الرياض",
        district = "العليا",
        street = "طريق الملك فهد",
        buildingNo = "1234",
        postalCode = "12211",
    };

    public static async Task<JsonElement> ProductAsync(this ScenarioFactory factory, string slug)
    {
        using var client = factory.Anonymous();
        var response = await client.GetAsync($"/v1/catalog/products/{slug}", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, slug);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.Clone();
    }

    public static async Task<Guid> ProductIdAsync(this ScenarioFactory factory, string slug) =>
        (await factory.ProductAsync(slug)).GetProperty("id").GetGuid();

    /// The id of a book format (`print` or `digital`).
    public static async Task<Guid> FormatIdAsync(this ScenarioFactory factory, string slug, string kind) =>
        (await factory.ProductAsync(slug)).GetProperty("book").GetProperty("formats").EnumerateArray()
            .Single(f => f.GetProperty("kind").GetString() == kind).GetProperty("id").GetGuid();

    public static object Buy(Guid productId, int quantity = 1, Guid? formatId = null) =>
        new { productId, bookFormatId = formatId, kind = "buy", mode = "units", quantity };

    public static object SponsorUnits(Guid productId, int quantity, string intent = "sadaqa") =>
        new { productId, kind = "sponsor", mode = "units", quantity, intent };

    public static object SponsorAmount(Guid productId, string amount, string intent = "sadaqa") =>
        new { productId, kind = "sponsor", mode = "amount", quantity = 1, amount, intent };

    public static object Donate(Guid productId, int quantity = 1, string intent = "sadaqa") =>
        new { productId, kind = "donate", mode = "units", quantity, intent };

    /// Adds the lines, quotes and checks out; returns the order reference and grand total (status `pending_payment`).
    public static async Task<PlacedOrder> OrderAsync(this ActorClient customer, params object[] lines)
    {
        foreach (var line in lines)
        {
            var (added, body) = await customer.PostAsync("/v1/cart/lines", line);
            added.IsSuccessStatusCode.ShouldBeTrue($"{(int)added.StatusCode} {body}");
        }

        return await customer.CheckoutAsync();
    }

    public static async Task<PlacedOrder> CheckoutAsync(this ActorClient customer, bool shipping = true)
    {
        var (quoted, quote) = await customer.PostAsync("/v1/checkout/quote", new { });
        quoted.StatusCode.ShouldBe(HttpStatusCode.OK, quote.ToString());
        var needsShipping = shipping && quote.GetProperty("shippingTotal").GetString() != "0.00"
                            || quote.TryGetProperty("requiresShipping", out var rs) && rs.GetBoolean();
        var (placed, order) = await customer.PostAsync("/v1/checkout", new
        {
            contact = new { fullName = "عميل تجربة", email = "customer@example.com" },
            isAnonymous = false,
            shippingAddress = needsShipping ? ShippingAddress : null,
            saveAddress = false,
            paymentMethod = "mada",
            acceptTerms = true,
            expectedGrandTotal = quote.GetProperty("grandTotal").GetString(),
            locale = "ar",
        }, new Dictionary<string, string> { ["Idempotency-Key"] = Guid.NewGuid().ToString() });
        placed.IsSuccessStatusCode.ShouldBeTrue($"{(int)placed.StatusCode} {order}");
        order.GetProperty("status").GetString().ShouldBe("pending_payment");
        return new PlacedOrder(order.GetProperty("reference").GetString()!, order.GetProperty("grandTotal").GetString()!, order.GetProperty("orderId").GetGuid());
    }

    /// Places and settles an order through the dev trigger, then runs the outbox.
    public static async Task<PlacedOrder> PaidOrderAsync(this ScenarioFactory factory, ActorClient customer, params object[] lines)
    {
        var order = await customer.OrderAsync(lines);
        (await factory.SettleAsync(order.Reference)).Status.ShouldBeOneOf("paid", "fulfilled");
        await factory.DrainOutboxAsync();
        return order;
    }
}

public sealed record PlacedOrder(string Reference, string GrandTotal, Guid OrderId);
