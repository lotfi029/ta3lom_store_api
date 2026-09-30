using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.Scenarios;

/// T-7 — endpoint contract sweeps over the live endpoint table (every route, not a hand-kept list), plus the checks of
/// Run-plan-review §3 T-7: Mapster `Compile()`, OpenAPI against 11 §12, catalogue search and caching, audit rows.
/// Success paths and business codes per endpoint are in the scenario classes (T-2 … T-6).
public sealed partial class EndpointContractTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Route(string Method, string Pattern, IReadOnlyList<string> Policies, bool JsonBody, bool Multipart);

    private IReadOnlyList<Route> Routes()
    {
        _ = factory.Anonymous();
        return [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && raw.StartsWith("/v1/", StringComparison.Ordinal))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m =>
            {
                var accepts = e.Metadata.GetOrderedMetadata<IAcceptsMetadata>().SelectMany(a => a.ContentTypes).ToList();
                return new Route(m, e.RoutePattern.RawText!, [.. e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).OfType<string>()],
                    accepts.Any(c => c.Contains("json", StringComparison.Ordinal)), accepts.Any(c => c.StartsWith("multipart/", StringComparison.Ordinal)));
            }))
            .DistinctBy(r => (r.Method, r.Pattern))];
    }

    private static string Path(string pattern) => Param().Replace(pattern, m => m.Value.Contains(":guid", StringComparison.Ordinal) ? Guid.NewGuid().ToString() : "x");

    private async Task<HttpClient> ActorForAsync(Route route) =>
        route.Pattern.StartsWith("/v1/admin/auth", StringComparison.Ordinal) ? factory.Anonymous()
        : route.Pattern.StartsWith("/v1/admin", StringComparison.Ordinal) ? (await factory.StaffAsync([.. Roles.All])).Http
        : (await factory.CustomerAsync()).Http;

    [Fact]
    public async Task Every_JSON_endpoint_answers_a_malformed_body_with_400_validation_failed()
    {
        var failures = new List<string>();
        // The TOTP routes authenticate the 5-minute MFA token before binding: covered by the S-05 tests.
        foreach (var route in Routes().Where(r => r.JsonBody && !r.Multipart && r.Method is "POST" or "PUT" or "PATCH" or "DELETE" && !r.Policies.Contains("staff-mfa")))
        {
            var client = await ActorForAsync(route);
            using var request = new HttpRequestMessage(new HttpMethod(route.Method), Path(route.Pattern))
            {
                Content = new StringContent("{ not json", Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            var response = await client.SendAsync(request, Ct);
            var body = await response.Content.ReadAsStringAsync(Ct);
            if (response.StatusCode != HttpStatusCode.BadRequest || !body.Contains("\"validation.failed\"", StringComparison.Ordinal))
            {
                failures.Add($"{route.Method} {route.Pattern} → {(int)response.StatusCode} {body[..Math.Min(body.Length, 120)]}");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_parameterless_GET_answers_200_for_the_right_caller()
    {
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/v1/catalog/live"] = "?slugs=python-kids,school-bag",
            ["/v1/admin/customers"] = "?mobile=0551000001",
            ["/v1/admin/audit"] = "?from=2026-01-01&to=2027-01-01",
        };
        var failures = new List<string>();
        foreach (var route in Routes().Where(r => r.Method == "GET" && !r.Pattern.Contains('{', StringComparison.Ordinal)))
        {
            var client = route.Policies.Count == 0 && !route.Pattern.StartsWith("/v1/me", StringComparison.Ordinal) && !route.Pattern.StartsWith("/v1/admin", StringComparison.Ordinal)
                ? factory.Anonymous()
                : await ActorForAsync(route);
            var response = await client.GetAsync(route.Pattern + queries.GetValueOrDefault(route.Pattern, ""), Ct);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                failures.Add($"GET {route.Pattern} → {(int)response.StatusCode} {(await response.Content.ReadAsStringAsync(Ct))[..Math.Min(120, (int)(response.Content.Headers.ContentLength ?? 120))]}");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_customer_route_refuses_an_anonymous_caller_with_401()
    {
        var anonymous = factory.Anonymous();
        var routes = Routes().Where(r => r.Policies.Contains(Policies.Customer)).ToList();
        routes.Count.ShouldBeGreaterThan(15);
        foreach (var route in routes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(route.Method), Path(route.Pattern));
            if (route.JsonBody)
            {
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            (await anonymous.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{route.Method} {route.Pattern}");
        }
    }

    [Fact]
    public void Every_Mapster_mapping_compiles() =>
        Should.NotThrow(() => factory.Services.GetRequiredService<TypeAdapterConfig>().Compile());

    [Fact]
    public async Task The_OpenAPI_document_has_every_endpoint_of_11_section_12_that_this_backend_owns()
    {
        var index = File.ReadAllText(RepoFile("store/11-api-contract.md")).Split("## 12. Endpoint index")[1];
        int[] paymentService = [43, 44, 89, 90, 91, 92];
        var expected = IndexRow().Matches(index)
            .Where(m => !paymentService.Contains(int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .Select(m => (Method: m.Groups[2].Value.ToLowerInvariant(), Path: Param().Replace(m.Groups[3].Value.Split('?')[0], "{}")))
            .Where(r => r.Path.StartsWith("/v1/", StringComparison.Ordinal))
            .ToList();
        var doc = JsonDocument.Parse(await factory.Anonymous().GetStringAsync("/openapi/v1.json", Ct)).RootElement.GetProperty("paths");
        var documented = doc.EnumerateObject().SelectMany(p => p.Value.EnumerateObject().Select(o => (Method: o.Name, Path: Param().Replace(p.Name, "{}")))).ToHashSet();

        expected.Count.ShouldBe(128);
        expected.Where(e => !documented.Contains(e)).Select(e => $"{e.Method} {e.Path}").ShouldBeEmpty();
    }

    [Fact]
    public async Task Catalogue_search_ignores_tashkeel_and_letter_variants_and_answers_304_to_a_matching_ETag()
    {
        var anonymous = factory.Anonymous();
        var (_, all) = await anonymous.SendJsonAsync(HttpMethod.Get, "/v1/catalog/products?fields=slug&pageSize=100");
        all.GetProperty("items").GetArrayLength().ShouldBe(15);
        var (_, tashkeel) = await anonymous.SendJsonAsync(HttpMethod.Get, "/v1/catalog/products?q=" + Uri.EscapeDataString("حِكَايَات"));
        tashkeel.GetProperty("items").EnumerateArray().ShouldContain(i => i.GetProperty("slug").GetString() == "letters-tales");
        var (_, taMarbuta) = await anonymous.SendJsonAsync(HttpMethod.Get, "/v1/catalog/products?q=" + Uri.EscapeDataString("مكتبه"));
        var (_, withTa) = await anonymous.SendJsonAsync(HttpMethod.Get, "/v1/catalog/products?q=" + Uri.EscapeDataString("مكتبة"));
        taMarbuta.GetProperty("total").GetInt32().ShouldBe(withTa.GetProperty("total").GetInt32());

        var first = await anonymous.GetAsync("/v1/catalog/products/python-kids", Ct);
        var etag = first.Headers.ETag;
        etag.ShouldNotBeNull();
        using var again = new HttpRequestMessage(HttpMethod.Get, "/v1/catalog/products/python-kids");
        again.Headers.IfNoneMatch.Add(etag);
        (await anonymous.SendAsync(again, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotModified);
        first.Headers.CacheControl!.Public.ShouldBeTrue();
    }

    [Fact]
    public async Task Catalogue_changes_are_audited_with_the_staff_actor()
    {
        var manager = await factory.StaffAsync(Roles.StoreManager);
        var admin = await factory.StaffAsync(Roles.Admin);
        var cause = await factory.ProductIdAsync("education-fund");

        (await manager.SendAsync(HttpMethod.Put, $"/v1/admin/products/{cause}/goal-seed", new { seedRaised = "1000.00", seedUnits = 0, seedDonors = 3, reason = "رصيد افتتاحي" }))
            .Response.IsSuccessStatusCode.ShouldBeTrue();
        (await admin.SendAsync(HttpMethod.Put, "/v1/admin/settings/shipping_flat_fee", new { value = "25.00" })).Response.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE actor = '{manager.Email}' AND entity_id = '{cause}'")).ShouldBeGreaterThanOrEqualTo(1);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM store.audit_log WHERE actor = '{admin.Email}'")).ShouldBeGreaterThanOrEqualTo(1);
    }

    private static string RepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(relative);
    }

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex Param();

    [GeneratedRegex(@"^\|\s*(\d+)\s*\|\s*([A-Z]+)\s*\|\s*`([^`]+)`", RegexOptions.Multiline)]
    private static partial Regex IndexRow();
}

/// T-7: the development tooling does not exist in Production (plan 14 §15).
public sealed class ProductionDevRoutesTests(ProductionApiFactory factory) : IClassFixture<ProductionApiFactory>
{
    [Theory]
    [InlineData("GET", "/dev/jobs")]
    [InlineData("GET", "/dev/sms")]
    [InlineData("GET", "/dev/mail")]
    [InlineData("POST", "/dev/orders/T3-26-000001/confirm-payment")]
    [InlineData("POST", "/dev/jobs/SponsorDigestJob/run")]
    [InlineData("POST", "/dev/lms/progress")]
    public async Task Dev_routes_are_not_mapped_in_production(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        }

        (await factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
