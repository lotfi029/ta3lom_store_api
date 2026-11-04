using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Payments.Fake;

namespace Ta3lom.Store.Api.IntegrationTests.Payments;

/// Development only (C-2): the fake hosted page and the clock override used by the web e2e.
public sealed class DevPaymentPageTests(DevelopmentApiFactory factory) : IClassFixture<DevelopmentApiFactory>
{
    [Fact]
    public async Task The_fake_hosted_page_shows_the_order_and_the_three_actions()
    {
        var store = factory.Services.GetRequiredService<FakePaymentStore>();
        var invoice = store.AddInvoice(new HostedPaymentRequest(Guid.NewGuid(), "T3-26-000042", 35000, "T3-26-000042",
            "http://localhost:4200/ar/orders/T3-26-000042?t=x", DateTimeOffset.UnixEpoch.AddYears(60)));

        var response = await factory.CreateClient().GetAsync($"/dev/payments/{invoice.Id}", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        html.ShouldContain("T3-26-000042");
        html.ShouldContain("350.00");
        html.ShouldContain("id=\"pay\"");
        html.ShouldContain("id=\"fail\"");
        html.ShouldContain("id=\"wrong\"");
        (await factory.CreateClient().GetAsync("/dev/payments/nope", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_clock_can_be_moved_forward_and_reset()
    {
        var client = factory.CreateClient();
        var moved = await client.PostAsJsonAsync("/dev/clock", new { advanceMinutes = 61 }, TestContext.Current.CancellationToken);
        var body = JsonDocument.Parse(await moved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        moved.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("offsetMinutes").GetDouble().ShouldBe(61);
        var reset = await client.PostAsJsonAsync("/dev/clock", new { reset = true }, TestContext.Current.CancellationToken);
        JsonDocument.Parse(await reset.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.GetProperty("offsetMinutes").GetDouble().ShouldBe(0);
    }
}
