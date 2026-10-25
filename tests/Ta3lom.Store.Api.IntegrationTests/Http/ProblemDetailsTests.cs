using System.Net;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Http;

/// RFC 9457 errors with a stable code, localised title/detail, W3C trace id.
public sealed class ProblemDetailsTests(ProbeApiFactory factory) : IClassFixture<ProbeApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unknown_route_is_404_problem_with_code_type_and_trace_id()
    {
        var response = await factory.CreateClient().GetAsync("/v1/does-not-exist", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.ReadProblemAsync();
        problem.Str("code").ShouldBe("http.not_found");
        problem.Str("type").ShouldBe($"{StoreApiFactory.PublicBaseUrl}/problems/http.not_found");
        problem.GetProperty("status").GetInt32().ShouldBe(404);
        problem.Str("traceId").ShouldMatch("^[0-9a-f]{32}$");
    }

    [Theory]
    [InlineData(null, "غير موجود")]
    [InlineData("ar", "غير موجود")]
    [InlineData("en", "Not found")]
    [InlineData("en-US,en;q=0.9", "Not found")]
    [InlineData("fr-FR", "غير موجود")]
    public async Task Title_follows_Accept_Language_with_Arabic_default(string? acceptLanguage, string expectedTitle)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/nope");
        if (acceptLanguage is not null)
        {
            request.Headers.Add("Accept-Language", acceptLanguage);
        }

        var problem = await (await factory.CreateClient().SendAsync(request, Ct)).ReadProblemAsync();

        problem.Str("title").ShouldBe(expectedTitle);
        problem.Str("detail").ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(410)]
    [InlineData(422)]
    [InlineData(429)]
    public async Task Result_errors_map_to_status_and_keep_code_and_extensions(int status)
    {
        var response = await factory.CreateClient().GetAsync($"/v1/__test/error/{status}", Ct);

        ((int)response.StatusCode).ShouldBe(status);
        var problem = await response.ReadProblemAsync();
        problem.Str("code").ShouldBe("test.code");
        problem.Str("type").ShouldBe($"{StoreApiFactory.PublicBaseUrl}/problems/test.code");
        problem.Str("max").ShouldBe("50");
        problem.Str("title").ShouldBe("Developer message");   // no localised entry for this code
    }

    [Fact]
    public async Task Result_success_is_200()
    {
        var response = await factory.CreateClient().GetAsync("/v1/__test/ok", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("\"fine\"");
    }

    [Fact]
    public async Task DomainException_is_409_with_its_code_and_no_internals()
    {
        var response = await factory.CreateClient().GetAsync("/v1/__test/throw/domain", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var text = await response.Content.ReadAsStringAsync(Ct);
        (await response.ReadProblemAsync()).Str("code").ShouldBe("unit.invalid_transition");
        text.ShouldNotContain("internal detail");
    }

    [Fact]
    public async Task Unexpected_exception_is_500_server_error_without_stack_or_message()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/__test/throw/crash");
        request.Headers.Add("Accept-Language", "en");

        var response = await factory.CreateClient().SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var text = await response.Content.ReadAsStringAsync(Ct);
        var problem = await response.ReadProblemAsync();
        problem.Str("code").ShouldBe(ErrorCodes.ServerError);
        problem.Str("title").ShouldBe("Unexpected error");
        text.ShouldNotContain("secret internals");
        text.ShouldNotContain("InvalidOperationException");
        text.ShouldNotContain("   at ");
    }

    [Fact]
    public async Task Method_not_allowed_uses_the_generic_http_error_code()
    {
        var response = await factory.CreateClient().DeleteAsync("/v1/__test/ok", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await response.ReadProblemAsync()).Str("code").ShouldBe(ErrorCodes.HttpError);
    }
}
