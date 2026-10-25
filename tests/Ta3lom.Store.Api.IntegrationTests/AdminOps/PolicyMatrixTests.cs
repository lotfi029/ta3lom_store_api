using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure.Scenario;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.IntegrationTests.AdminOps;

/// Every `/v1/admin` route (except the sign-in routes under `/v1/admin/auth`) × the four staff roles + anonymous. The
/// expected roles are a literal table (F4), not derived from the policies: a role outside the row gets 403, anonymous
/// 401, and the table must list exactly the routes the API maps, so a new admin route fails here until it is added.
public sealed class PolicyMatrixTests(ScenarioFactory factory) : IClassFixture<ScenarioFactory>
{
    private static readonly string[] All = [Roles.Admin, Roles.StoreManager, Roles.Finance, Roles.Viewer];
    private static readonly string[] Catalogue = [Roles.Admin, Roles.StoreManager];
    private static readonly string[] Donors = [Roles.Admin, Roles.StoreManager];
    private static readonly string[] Orders = [Roles.Admin, Roles.Finance];
    private static readonly string[] Templates = [Roles.Admin, Roles.StoreManager];
    private static readonly string[] AdminOnly = [Roles.Admin];

    /// The plan's access table, one row per route.
    private static readonly Dictionary<string, string[]> Expected = new(StringComparer.Ordinal)
    {
        ["GET /v1/admin/me"] = All,
        ["GET /v1/admin/dashboard"] = All,
        ["GET /v1/admin/categories"] = All,
        ["POST /v1/admin/categories"] = Catalogue,
        ["PUT /v1/admin/categories/order"] = Catalogue,
        ["PUT /v1/admin/categories/{id}"] = Catalogue,
        ["GET /v1/admin/projects"] = All,
        ["POST /v1/admin/projects"] = Catalogue,
        ["GET /v1/admin/projects/{id}"] = All,
        ["PUT /v1/admin/projects/{id}"] = Catalogue,
        ["PUT /v1/admin/projects/{id}/options"] = Catalogue,
        ["POST /v1/admin/projects/{id}/publish"] = Catalogue,
        ["POST /v1/admin/projects/{id}/archive"] = Catalogue,
        ["GET /v1/admin/projects/{id}/preview"] = All,
        ["GET /v1/admin/media"] = All,
        ["POST /v1/admin/media"] = Catalogue,
        ["PATCH /v1/admin/media/{id}"] = Catalogue,
        ["GET /v1/admin/customers"] = All,
        ["GET /v1/admin/customers/{id}"] = All,
        ["POST /v1/admin/customers/{id}/block"] = Donors,
        ["POST /v1/admin/customers/{id}/unblock"] = Donors,
        ["GET /v1/admin/orders"] = All,
        ["GET /v1/admin/orders/{reference}"] = All,
        ["POST /v1/admin/orders/{reference}/cancel"] = Orders,
        ["GET /v1/admin/templates"] = Templates,
        ["POST /v1/admin/templates/test"] = Templates,
        ["PUT /v1/admin/templates/{key}/{channel}/{locale}"] = Templates,
        ["GET /v1/admin/settings"] = AdminOnly,
        ["PUT /v1/admin/settings/{key}"] = AdminOnly,
        ["GET /v1/admin/audit"] = AdminOnly,
        ["GET /v1/admin/staff"] = AdminOnly,
        ["POST /v1/admin/staff"] = AdminOnly,
        ["PUT /v1/admin/staff/{id}"] = AdminOnly,
        ["POST /v1/admin/staff/{id}/reset-password"] = AdminOnly,
        ["POST /v1/admin/staff/{id}/reset-totp"] = AdminOnly,
        ["POST /v1/admin/staff/{id}/unlock"] = AdminOnly,
    };

    private static string Key(AdminRoute route) =>
        $"{route.Method} {System.Text.RegularExpressions.Regex.Replace(route.Pattern, ":[a-z]+", "").TrimEnd('/')}";

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

    /// What the policies on the route allow (only to compare with the literal table).
    private static IReadOnlyList<string> PolicyRoles(AdminRoute route) =>
        [.. Roles.All.Where(role => route.Policies.All(p => !Policies.StaffRoles.TryGetValue(p, out var roles) || roles.Contains(role)))];

    [Fact]
    public void The_literal_table_lists_exactly_the_mapped_admin_routes_and_matches_their_policies()
    {
        var routes = AdminRoutes();

        routes.Select(Key).OrderBy(k => k, StringComparer.Ordinal).ShouldBe(Expected.Keys.OrderBy(k => k, StringComparer.Ordinal));
        routes.Where(r => !PolicyRoles(r).OrderBy(x => x).SequenceEqual(Expected[Key(r)].OrderBy(x => x)))
            .Select(r => $"{Key(r)}: policies allow {string.Join(",", PolicyRoles(r))}").ShouldBeEmpty();
    }

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
            var allowed = Expected[Key(route)];
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
