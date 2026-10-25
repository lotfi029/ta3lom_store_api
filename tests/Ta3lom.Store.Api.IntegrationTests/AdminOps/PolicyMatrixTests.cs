using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.AdminOps;

/// Every `/v1/admin` route (except the sign-in routes under `/v1/admin/auth`) × the four staff roles + anonymous: the
/// route's policies decide, a role outside them gets 403, anonymous gets 401. New admin routes are picked up from the
/// endpoint table, so a route without a staff policy fails here.
public sealed class PolicyMatrixTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private sealed record AdminRoute(string Method, string Pattern, IReadOnlyList<string> Policies);

    private IReadOnlyList<AdminRoute> AdminRoutes()
    {
        factory.CreateClient().Dispose();                                   // start the host
        return [.. factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && raw.StartsWith("/v1/admin", StringComparison.Ordinal)
                        && !raw.StartsWith("/v1/admin/auth", StringComparison.Ordinal))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(m => new AdminRoute(m, e.RoutePattern.RawText!,
                [.. e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).OfType<string>()])))
            .OrderBy(r => r.Pattern).ThenBy(r => r.Method)];
    }

    private static IReadOnlyList<string> AllowedRoles(AdminRoute route) =>
        [.. Roles.All.Where(role => route.Policies.All(p => !Policies.StaffRoles.TryGetValue(p, out var roles) || roles.Contains(role)))];

    private static string Concrete(string pattern) => System.Text.RegularExpressions.Regex.Replace(pattern, @"\{(\w+)(:[^}]*)?\}", m => m.Groups[1].Value switch
    {
        "id" => Guid.NewGuid().ToString(),
        "reference" => "T3-00-000000",
        "channel" => "sms",
        "locale" => "ar",
        _ => "matrix",
    });

    [Fact]
    public void Every_admin_route_has_a_staff_policy()
    {
        var routes = AdminRoutes();

        routes.Count.ShouldBeGreaterThan(30);
        routes.Where(r => !r.Policies.Any(p => Policies.StaffRoles.ContainsKey(p) || p.StartsWith("staff", StringComparison.Ordinal)))
            .Select(r => $"{r.Method} {r.Pattern}").ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_role_reaches_exactly_the_routes_its_policies_allow()
    {
        var clients = new Dictionary<string, HttpClient>(StringComparer.Ordinal);
        foreach (var role in Roles.All)
        {
            clients[role] = (await factory.StaffAsync(role)).Http;
        }

        var anonymous = factory.CreateClient();
        List<string> wrong = [];
        foreach (var route in AdminRoutes())
        {
            var path = Concrete(route.Pattern);
            var allowed = AllowedRoles(route);
            foreach (var (role, client) in clients)
            {
                var status = await SendAsync(client, route.Method, path);
                var expectForbidden = !allowed.Contains(role);
                if (expectForbidden ? status != HttpStatusCode.Forbidden : status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                {
                    wrong.Add($"{route.Method} {route.Pattern} as {role}: {(int)status}");
                }
            }

            var anon = await SendAsync(anonymous, route.Method, path);
            if (anon != HttpStatusCode.Unauthorized)
            {
                wrong.Add($"{route.Method} {route.Pattern} anonymous: {(int)anon}");
            }
        }

        wrong.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("POST", "/v1/admin/customers/{id}/block", new[] { Roles.Admin, Roles.StoreManager })]
    [InlineData("POST", "/v1/admin/orders/{reference}/cancel", new[] { Roles.Admin, Roles.Finance })]
    [InlineData("PUT", "/v1/admin/templates/{key}/{channel}/{locale}", new[] { Roles.Admin, Roles.StoreManager })]
    [InlineData("GET", "/v1/admin/audit", new[] { Roles.Admin })]
    [InlineData("GET", "/v1/admin/settings", new[] { Roles.Admin })]
    [InlineData("GET", "/v1/admin/dashboard", new[] { Roles.Admin, Roles.StoreManager, Roles.Finance, Roles.Viewer })]
    [InlineData("POST", "/v1/admin/projects", new[] { Roles.Admin, Roles.StoreManager })]
    public void The_matrix_matches_the_plan(string method, string pattern, string[] roles)
    {
        var route = AdminRoutes().Single(r => r.Method == method && System.Text.RegularExpressions.Regex.Replace(r.Pattern, ":[a-z]+", "").TrimEnd('/') == pattern);

        AllowedRoles(route).ShouldBe(roles, ignoreOrder: true);
    }

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST" && path.TrimEnd('/') == "/v1/admin/media")
        {
            request.Content = new MultipartFormDataContent { { new StringContent("image"), "kind" } };   // the route accepts multipart only
        }
        else if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = JsonContent.Create(new { });
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }
}
