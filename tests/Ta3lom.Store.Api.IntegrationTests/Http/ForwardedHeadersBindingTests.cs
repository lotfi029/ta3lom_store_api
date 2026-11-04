using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Api.IntegrationTests.Infrastructure;

namespace Ta3lom.Store.Api.IntegrationTests.Http;

/// F4 (plan §5): the API trusts X-Forwarded-* only from the edge network. compose.yaml sets
/// `Store__ForwardedHeaders__KnownNetworks__0` from `.env` (EDGE_SUBNET): that value must replace the default and be the
/// only trusted network, with no loopback default left.
public sealed class ForwardedHeadersBindingTests(EdgeSubnetFactory factory) : IClassFixture<EdgeSubnetFactory>
{
    [Fact]
    public void The_edge_subnet_from_the_environment_is_the_only_trusted_proxy_network()
    {
        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        options.KnownIPNetworks.Select(n => $"{n.BaseAddress}/{n.PrefixLength}").ShouldBe(["172.31.0.0/24"]);
        options.KnownProxies.ShouldBeEmpty();
        options.ForwardedHeaders.ShouldBe(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
    }
}

/// The Production host with `Store__ForwardedHeaders__KnownNetworks__0` as compose sets it.
public sealed class EdgeSubnetFactory : ProductionApiFactory
{
    protected override IReadOnlyDictionary<string, string?> Settings =>
        new Dictionary<string, string?>(base.Settings) { ["Store:ForwardedHeaders:KnownNetworks:0"] = "172.31.0.0/24" };
}
