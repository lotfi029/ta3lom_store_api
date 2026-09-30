using System.Net.Http.Json;
using System.Text.Json;

namespace Ta3lom.Store.Api.IntegrationTests.Infrastructure;

public static class ProblemJson
{
    public static async Task<JsonElement> ReadProblemAsync(this HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    public static string Str(this JsonElement e, string name) => e.GetProperty(name).GetString()!;
}
