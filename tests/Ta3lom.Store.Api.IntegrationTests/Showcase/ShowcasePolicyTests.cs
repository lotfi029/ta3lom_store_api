using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.IntegrationTests.Showcase;

/// SM-8: the showcase policy table. A literal list of the routes switched off in showcase and the code they answer; the
/// list must equal the routes carrying the store-feature filter, so a new payment or sign-in route fails here until it is
/// added. Each route is called by an actor its authorization policy admits (donor, finance, anonymous) and must answer
/// 409 with the code — never 2xx, never a replay.
public sealed class ShowcasePolicyTests(ShowcaseScenarioFactory factory) : IClassFixture<ShowcaseScenarioFactory>
{
    private enum Actor
    {
        Anonymous,
        Donor,
        Finance,
    }

    private static readonly Dictionary<string, (Actor Actor, string Code)> Expected = new(StringComparer.Ordinal)
    {
        ["POST /v1/cart/lines"] = (Actor.Anonymous, ErrorCodes.StorePaymentsDisabled),
        ["POST /v1/checkout"] = (Actor.Donor, ErrorCodes.StorePaymentsDisabled),
        ["POST /v1/orders/{reference}/pay"] = (Actor.Anonymous, ErrorCodes.StorePaymentsDisabled),
        ["POST /v1/admin/orders/{reference}/refunds"] = (Actor.Finance, ErrorCodes.StorePaymentsDisabled),
        ["POST /v1/admin/orders/{reference}/review/accept"] = (Actor.Finance, ErrorCodes.StorePaymentsDisabled),
        ["POST /v1/admin/orders/{reference}/review/refund"] = (Actor.Finance, ErrorCodes.StorePaymentsDisabled),
        ["POST /v1/admin/orders/{reference}/receipt/resend"] = (Actor.Finance, ErrorCodes.StorePaymentsDisabled),
        ["POST /v1/auth/otp/request"] = (Actor.Anonymous, ErrorCodes.StoreSignInDisabled),
        ["POST /v1/auth/otp/verify"] = (Actor.Anonymous, ErrorCodes.StoreSignInDisabled),
        ["POST /v1/auth/refresh"] = (Actor.Anonymous, ErrorCodes.StoreSignInDisabled),
    };

    private IReadOnlyList<string> FeatureRoutes()
    {
        factory.CreateClient().Dispose();
        return [.. factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<StoreFeatureMetadata>() is not null)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(m => $"{m} {System.Text.RegularExpressions.Regex.Replace(e.RoutePattern.RawText!, ":[a-z]+", "").TrimEnd('/')}"))];
    }

    [Fact]
    public void The_table_lists_exactly_the_routes_behind_a_store_feature() =>
        FeatureRoutes().Order(StringComparer.Ordinal).ShouldBe(Expected.Keys.Order(StringComparer.Ordinal));

    [Fact]
    public async Task Every_disabled_route_answers_409_with_its_code_and_writes_nothing()
    {
        var donor = (await factory.CustomerAsync()).Http;
        var finance = (await factory.StaffAsync(Roles.Finance)).Http;
        var anonymous = factory.CreateClient();
        List<string> wrong = [];
        foreach (var (route, (actor, code)) in Expected)
        {
            var parts = route.Split(' ', 2);
            using var request = new HttpRequestMessage(new HttpMethod(parts[0]), parts[1].Replace("{reference}", "T3-00-000000", StringComparison.Ordinal))
            {
                Content = JsonContent.Create(new { amount = "10.00", reason = "showcase", version = 1, mobile = "0590000001", code = "123456" }),
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            var client = actor switch { Actor.Donor => donor, Actor.Finance => finance, _ => anonymous };
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            if (response.StatusCode != HttpStatusCode.Conflict || !body.Contains($"\"code\":\"{code}\"", StringComparison.Ordinal))
            {
                wrong.Add($"{route} as {actor}: {(int)response.StatusCode} {body}");
            }
        }

        wrong.ShouldBeEmpty();
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.idempotency_key")).ShouldBe(0);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.orders")).ShouldBe(0);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM store.cart")).ShouldBe(0);
    }
}
