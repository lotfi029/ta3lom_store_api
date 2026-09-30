using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.Scenarios;

/// T-6 — the role × route matrix (RUL-62, 11 §1.2): every `/v1/admin` route is called as each of the six roles and
/// anonymously. The expectation comes from the route's own policies and `Policies.StaffRoles`: a role outside the policy
/// gets 403, a role inside never gets 401/403 (nor a 5xx), anonymous always gets 401. Route parameters are unknown ids,
/// so nothing is changed.
public sealed partial class PolicyMatrixTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private sealed record AdminRoute(string Method, string Pattern, IReadOnlyList<string> Policies, bool Multipart);

    private IReadOnlyList<AdminRoute> AdminRoutes()
    {
        _ = factory.Anonymous();                                                                          // start the host
        return [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && raw.StartsWith("/v1/admin", StringComparison.Ordinal) && !raw.StartsWith("/v1/admin/auth", StringComparison.Ordinal))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => new AdminRoute(
                m,
                e.RoutePattern.RawText!,
                [.. e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).OfType<string>().Distinct()],
                e.Metadata.GetOrderedMetadata<IAcceptsMetadata>().Any(a => a.ContentTypes.Any(c => c.StartsWith("multipart/", StringComparison.Ordinal))))))
            .Where(r => r.Method != "HEAD" && r.Method != "OPTIONS")
            .DistinctBy(r => (r.Method, r.Pattern))];
    }

    private static string Path(string pattern) => Param().Replace(pattern, m => m.Groups[1].Value switch
    {
        "reference" => "T3-26-999999",
        "name" => "sales",
        "key" => "no_such_key",
        "channel" => "sms",
        "locale" => "ar",
        "number" => "INV-2026-999999",
        var p when m.Value.Contains(":guid", StringComparison.Ordinal) || p.EndsWith("id", StringComparison.OrdinalIgnoreCase) => Guid.NewGuid().ToString(),
        _ => "x",
    });

    private static async Task<HttpStatusCode> CallAsync(HttpClient client, AdminRoute route)
    {
        using var request = new HttpRequestMessage(new HttpMethod(route.Method), Path(route.Pattern));
        if (route.Method is "POST" or "PUT" or "PATCH")
        {
            request.Content = route.Multipart ? new MultipartFormDataContent { { new StringContent("x"), "kind" } } : JsonContent.Create(new { });
        }

        return (await client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode;
    }

    [Fact]
    [Rule("RUL-62")]
    public async Task Every_admin_route_answers_each_role_as_its_policy_says()
    {
        var routes = AdminRoutes();
        routes.Count.ShouldBeGreaterThanOrEqualTo(80);
        routes.Where(r => r.Policies.Count == 0 && r.Pattern != "/v1/admin/me").Select(r => $"{r.Method} {r.Pattern}").ShouldBeEmpty("every admin route names a policy");

        var failures = new List<string>();
        foreach (var role in Roles.All)
        {
            var client = (await factory.StaffAsync(role)).Http;
            foreach (var route in routes)
            {
                var allowed = route.Policies.All(p => Policies.StaffRoles.TryGetValue(p, out var roles) && roles.Contains(role));
                var status = await CallAsync(client, route);
                var ok = allowed
                    ? status is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) && (int)status < 500
                    : status == HttpStatusCode.Forbidden;
                if (!ok)
                {
                    failures.Add($"{role} {route.Method} {route.Pattern} [{string.Join(",", route.Policies)}] → {(int)status} (expected {(allowed ? "allowed" : "403")})");
                }
            }
        }

        var anonymous = factory.Anonymous();
        foreach (var route in routes)
        {
            if (await CallAsync(anonymous, route) != HttpStatusCode.Unauthorized)
            {
                failures.Add($"anonymous {route.Method} {route.Pattern} → not 401");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    [Rule("RUL-62")]
    public void Segregation_of_duties_is_in_the_policy_table()
    {
        Policies.StaffRoles[Policies.StaffAllocate].ShouldBe([Roles.AllocationOfficer]);
        Policies.StaffRoles[Policies.StaffRevealPii].ShouldBe([Roles.AllocationOfficer]);
        Policies.StaffRoles[Policies.StaffRefund].ShouldBe([Roles.Finance]);
        Policies.StaffRoles[Policies.StaffAdmin].ShouldBe([Roles.Admin]);
        Policies.StaffRoles[Policies.StaffFulfilment].ShouldNotContain(Roles.Finance);                  // finance cannot ship
    }

    [Fact]
    [Rule("RUL-62")]
    public async Task Customer_routes_refuse_staff_tokens_and_staff_routes_refuse_customer_tokens()
    {
        var customer = (await factory.CustomerAsync()).Http;
        var staff = (await factory.StaffAsync(Roles.Admin)).Http;
        var ct = TestContext.Current.CancellationToken;

        foreach (var path in new[] { "/v1/me", "/v1/me/orders", "/v1/me/giving", "/v1/me/addresses", "/v1/me/export" })
        {
            (await staff.GetAsync(path, ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
            (await customer.GetAsync(path, ct)).StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }

        (await customer.GetAsync("/v1/admin/orders", ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [GeneratedRegex(@"\{([a-zA-Z]+)(?::[a-z]+)?\}")]
    private static partial Regex Param();
}
