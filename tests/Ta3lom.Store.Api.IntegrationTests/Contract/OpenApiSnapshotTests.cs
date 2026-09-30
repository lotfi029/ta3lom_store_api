using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;

namespace Ta3lom.Store.Api.IntegrationTests.Contract;

/// The generated OpenAPI document must equal the committed openapi/v1.json (09 §10.3).
/// Intentional changes: run with UPDATE_OPENAPI_SNAPSHOT=1 and commit the file in the same PR.
/// Uses the factory WITHOUT the test probe routes.
public sealed class OpenApiSnapshotTests(StoreApiFactory factory) : IClassFixture<StoreApiFactory>
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task Generated_document_matches_the_committed_snapshot()
    {
        var json = await factory.CreateClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var generated = Normalise(json);
        var path = Path.Combine(RepositoryRoot(), "openapi", "v1.json");

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI_SNAPSHOT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, generated, TestContext.Current.CancellationToken);
        }

        File.Exists(path).ShouldBeTrue($"{path} is missing; run the tests with UPDATE_OPENAPI_SNAPSHOT=1");
        Normalise(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))
            .ShouldBe(generated, "openapi/v1.json is out of date; run the tests with UPDATE_OPENAPI_SNAPSHOT=1 and commit the change");
    }

    private static string Normalise(string json) =>
        JsonNode.Parse(json)!.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ta3lom.Store.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Ta3lom.Store.slnx not found above the test output");
    }
}
