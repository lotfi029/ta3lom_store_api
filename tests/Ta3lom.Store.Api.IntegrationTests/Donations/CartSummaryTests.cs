using System.Net;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;

namespace Ta3lom.Store.Api.IntegrationTests.Donations;

/// API-5: GET /v1/cart/summary is priced like GET /v1/cart (the catalogue as it is now), in a constant number of queries,
/// and is never cached.
public sealed class CartSummaryTests(CommandCountingFactory factory) : IClassFixture<CommandCountingFactory>
{
    [Fact]
    [Rule("D-05")]
    public async Task The_summary_total_is_the_repriced_cart_total()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();
        (await donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 3 })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await factory.ExecuteSqlAsync($"UPDATE store.project_option SET amount = 9.00 WHERE id = '{c.OptionId}'");

        var cart = (await donor.GetAsync("/v1/cart")).Body;
        var summary = await donor.GetAsync("/v1/cart/summary");

        cart.Str("totalAmount").ShouldBe("27.00");
        summary.Body.Str("totalAmount").ShouldBe(cart.Str("totalAmount"));
        summary.Body.GetProperty("lineCount").GetInt32().ShouldBe(1);
        summary.Response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task The_summary_runs_a_constant_number_of_queries()
    {
        var c = await DonationCatalog.CreateAsync(factory);
        var donor = await factory.CustomerAsync();
        await donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, optionId = c.OptionId, quantity = 1 });
        var one = await factory.Commands.CountAsync(() => donor.GetAsync("/v1/cart/summary"));

        await donor.PostAsync("/v1/cart/lines", new { projectId = c.OptionsOnlyProjectId, optionId = c.OptionsOnlyOptionId, quantity = 1 });
        await donor.PostAsync("/v1/cart/lines", new { projectId = c.ProjectId, amount = "50.00" });
        await donor.PostAsync("/v1/cart/lines", new { categoryId = c.CategoryId, amount = "20.00" });
        var four = await factory.Commands.CountAsync(() => donor.GetAsync("/v1/cart/summary"));

        four.ShouldBe(one);
    }
}
