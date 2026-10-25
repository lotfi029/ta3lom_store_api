using System.Net.Http.Json;
using System.Text.Json;
using Ta3lom.Store.Api.IntegrationTests.Catalog;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.Donations;

/// A published catalogue for the donation tests: one project with two options and an open amount, one options-only
/// project, and a category that accepts general gifts.
internal sealed record DonationCatalog(ActorClient Admin, Guid CategoryId, Guid ProjectId, Guid OptionId, Guid BigOptionId, Guid OptionsOnlyProjectId,
    Guid OptionsOnlyOptionId, JsonElement Project)
{
    public static async Task<DonationCatalog> CreateAsync(ScenarioFactory factory)
    {
        var admin = await factory.StaffAsync(Roles.Admin);
        var build = new CatalogBuilder(admin);
        var category = await build.CategoryAsync();
        var categoryId = Guid.Parse(category.Str("id"));
        var project = await build.PublishAsync(await build.OptionsAsync(await build.DraftAsync(categoryId), ("نسخة", "4.00", true), ("مئة نسخة", "400.00", true)));
        var optionsOnly = await build.PublishAsync(await build.OptionsAsync(await build.DraftAsync(categoryId, allowOpenAmount: false), ("كفالة شهر", "25.00", true)));
        var options = project.GetProperty("options").EnumerateArray().ToList();
        return new DonationCatalog(admin, categoryId, Guid.Parse(project.Str("id")), Guid.Parse(options[0].Str("id")), Guid.Parse(options[1].Str("id")),
            Guid.Parse(optionsOnly.Str("id")), Guid.Parse(optionsOnly.GetProperty("options")[0].Str("id")), project);
    }
}

internal static class HttpJson
{
    public static async Task<(HttpResponseMessage Response, JsonElement Body)> SendJsonAsync(this HttpClient client, HttpMethod method, string path,
        object? body = null, IDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        foreach (var (k, v) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.TryAddWithoutValidation(k, v);
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response, text.Length > 0 && text[0] is '{' or '[' ? JsonDocument.Parse(text).RootElement.Clone() : default);
    }

    public static Dictionary<string, string> Key(string? key = null) => new() { ["Idempotency-Key"] = key ?? $"key-{Guid.NewGuid():N}" };
}
