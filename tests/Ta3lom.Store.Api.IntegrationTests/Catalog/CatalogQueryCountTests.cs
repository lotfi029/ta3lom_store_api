using System.Net.Http.Json;
using System.Text.Json;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.Catalog;

/// API-9: cards and their options come from the listing's own queries — never one query per card.
public sealed class CatalogQueryCountTests(CommandCountingFactory factory) : IClassFixture<CommandCountingFactory>
{
    /// The response cache keys on the known query parameters, so each count uses a page size not requested before.
    private async Task<(int Commands, int Items)> ListAsync(string slug, int pageSize)
    {
        JsonElement body = default;
        var commands = await factory.Commands.CountAsync(async () =>
            body = (await factory.Anonymous().GetFromJsonAsync<JsonElement>($"/v1/catalog/projects?category={slug}&pageSize={pageSize}",
                TestContext.Current.CancellationToken))!);
        return (commands, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task A_listing_of_twelve_cards_runs_as_many_queries_as_a_listing_of_one()
    {
        var build = new CatalogBuilder(await factory.StaffAsync(Roles.Admin));
        var category = await build.CategoryAsync();
        var categoryId = Guid.Parse(category.Str("id"));
        await build.PublishAsync(await build.OptionsAsync(await build.DraftAsync(categoryId), ("نسخة", "4.00", true), ("خمس", "5.00", true)));
        await ListAsync(category.Str("slug"), 19);                                                 // warms the settings cache
        var one = await ListAsync(category.Str("slug"), 20);

        for (var i = 0; i < 11; i++)
        {
            await build.PublishAsync(await build.OptionsAsync(await build.DraftAsync(categoryId), ("نسخة", "4.00", true), ("خمس", "5.00", true)));
        }

        var twelve = await ListAsync(category.Str("slug"), 21);

        one.Items.ShouldBe(1);
        twelve.Items.ShouldBe(12);
        twelve.Commands.ShouldBe(one.Commands);
    }
}
