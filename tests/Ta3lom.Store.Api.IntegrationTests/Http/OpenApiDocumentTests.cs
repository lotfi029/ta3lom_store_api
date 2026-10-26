using System.Net;
using System.Text.Json;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;

namespace Ta3lom.Store.Api.IntegrationTests.Http;

/// API-7: the OpenAPI document (Development only) parses and describes the web's contract; CI publishes it as the
/// `store-api-openapi` artifact (set `OPENAPI_OUT` to write it).
public sealed class OpenApiDocumentTests(DevelopmentApiFactory factory) : IClassFixture<DevelopmentApiFactory>
{
    [Fact]
    public async Task The_document_describes_the_contract_and_is_written_when_asked()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;

        var paths = root.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        paths.ShouldContain("/v1/catalog/projects");
        paths.ShouldContain("/v1/cart/lines");
        paths.ShouldContain("/v1/checkout");
        paths.ShouldContain("/v1/orders/{reference}/status");
        paths.ShouldContain("/v1/admin/projects/{id}/options");

        var schemas = root.GetProperty("components").GetProperty("schemas");
        var card = schemas.GetProperty("ProjectCard");
        Required(card).ShouldContain("options");                                                           // API-9: always present
        card.GetProperty("properties").GetProperty("options").GetProperty("type").GetString().ShouldBe("array");
        Types(card.GetProperty("properties").GetProperty("coverAltEn")).ShouldBe(["null", "string"], ignoreOrder: true);   // API-10
        Types(schemas.GetProperty("CategorySummary").GetProperty("properties").GetProperty("imageAltEn")).ShouldBe(["null", "string"], ignoreOrder: true);

        if (Environment.GetEnvironmentVariable("OPENAPI_OUT") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, text, ct);
        }
    }

    private static List<string?> Required(JsonElement schema) => [.. schema.GetProperty("required").EnumerateArray().Select(r => r.GetString())];

    /// `type` is a string or (for a nullable property) an array of strings.
    private static List<string?> Types(JsonElement property) =>
        property.GetProperty("type") is { ValueKind: JsonValueKind.Array } many ? [.. many.EnumerateArray().Select(t => t.GetString())] : [property.GetProperty("type").GetString()];
}
