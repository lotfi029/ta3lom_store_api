using Ta3lom.Store.Infrastructure.Services.Security;

namespace Ta3lom.Store.Api.IntegrationTests.Http;

/// A-7: the unit of the per-IP limits (OTP per IP, every rate-limiter policy keyed on the client).
public sealed class ClientNetworkTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:3:4:5:6", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::ffff", "2001:db8:1:2::/64")]
    [InlineData("not-an-ip", "not-an-ip")]
    public void IPv4_counts_per_address_and_IPv6_per_64_prefix(string ip, string network) =>
        Pseudonymizer.ClientNetwork(ip).ShouldBe(network);
}
