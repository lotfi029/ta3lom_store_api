using System.Net;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.Scenarios;

/// T-1: the scenario host itself — own database, minted tokens, dev-trigger settlement, outbox on demand, sinks.
public sealed class ScenarioInfrastructureTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    [Fact]
    public async Task Customer_and_staff_clients_are_authenticated()
    {
        var customer = await factory.CustomerAsync();
        (await customer.GetAsync("/v1/me")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var admin = await factory.StaffAsync(Roles.Admin);
        var (me, body) = await admin.GetAsync("/v1/admin/me");
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("email").GetString().ShouldBe(admin.Email);
    }

    [Fact]
    public async Task An_order_settles_through_the_dev_trigger_and_the_outbox_sends_its_messages()
    {
        var customer = await factory.CustomerAsync();
        var course = await factory.ProductIdAsync("smart-study");

        var order = await factory.PaidOrderAsync(customer, Shop.Buy(course));

        (await factory.ScalarAsync<string>($"SELECT status FROM store.orders WHERE reference = '{order.Reference}'")).ShouldBeOneOf("paid", "fulfilled");
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.outbox_message WHERE processed_at IS NULL")).ShouldBe(0);
        factory.SmsTo(customer.Mobile!).ShouldContain(m => m.Body.Contains(order.Reference, StringComparison.Ordinal));
        (await factory.MailAsync()).ShouldContain(m => m.Subject.Contains(order.Reference, StringComparison.Ordinal));
        (await factory.IntegrityFailuresAsync()).ShouldBeEmpty();
    }
}
