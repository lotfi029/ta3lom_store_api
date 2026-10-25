using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Infrastructure.Services.Security;

/// HMAC-SHA256 with the hashing key, lower-case hex (64 characters). Each kind of value has its own input prefix, so an IP
/// hash can never collide with a mobile or e-mail hash.
internal sealed class Pseudonymizer(IOptions<HashingOptions> options) : IPseudonymizer
{
    private readonly byte[] _key = Convert.FromBase64String(options.Value.KeyBase64);

    public string HashIp(string ip) => Hex("ip:" + ClientNetwork(ip));

    public string HashMobile(string mobileE164) => Hex("mobile:" + mobileE164);

    public string HashEmail(string email) => Hex("email:" + (email ?? string.Empty).Trim().ToLowerInvariant());

    /// A-7: what counts as one client for the per-IP limits — an IPv4 address (also when IPv4-mapped), or the /64 of an IPv6
    /// address (one subscriber's block; rotating addresses inside it does not reset the limits).
    internal static string ClientNetwork(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address))
        {
            return ip;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    private string Hex(string value) => Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value)));
}
