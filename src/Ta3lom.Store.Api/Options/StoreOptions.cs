using System.ComponentModel.DataAnnotations;
using System.Net;

namespace Ta3lom.Store.Api.Options;

/// The `Store` configuration section (09 §7.2). Validated at start-up: the app refuses to run misconfigured.
public sealed class StoreOptions
{
    public const string Section = "Store";

    /// e.g. https://store-api.ta3lom.sa — used for ProblemDetails `type` URIs.
    [Required]
    [Url]
    public string PublicBaseUrl { get; set; } = string.Empty;

    [Url]
    public string? WebBaseUrl { get; set; }

    public CorsSettings Cors { get; set; } = new();

    public ForwardedHeadersSettings ForwardedHeaders { get; set; } = new();

    public RateLimitSettings RateLimits { get; set; } = new();

    public TelemetrySettings Telemetry { get; set; } = new();

    /// Returns every problem found, or an empty list (used by ValidateOnStart).
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (Cors.Origins.Count == 0)
        {
            problems.Add("Store:Cors:Origins needs at least one origin");
        }

        foreach (var origin in Cors.Origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.PathAndQuery != "/" || origin.EndsWith('/'))
            {
                problems.Add($"Store:Cors:Origins entry '{origin}' must be a scheme://host[:port] origin");
            }
        }

        foreach (var network in ForwardedHeaders.KnownNetworks)
        {
            if (!IPNetwork.TryParse(network, out _))
            {
                problems.Add($"Store:ForwardedHeaders:KnownNetworks entry '{network}' is not a CIDR network");
            }
        }

        foreach (var proxy in ForwardedHeaders.KnownProxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                problems.Add($"Store:ForwardedHeaders:KnownProxies entry '{proxy}' is not an IP address");
            }
        }

        var p = RateLimits.Public;
        if (p.TokenLimit < 1 || p.TokensPerPeriod < 1 || p.ReplenishmentPeriodSeconds < 1)
        {
            problems.Add("Store:RateLimits:Public values must be positive");
        }

        return problems;
    }
}

public sealed class CorsSettings
{
    /// Allowed browser origins (08 §3.1): per environment in appsettings.{Environment}.json.
    public IList<string> Origins { get; init; } = [];
}

public sealed class ForwardedHeadersSettings
{
    /// Networks of the reverse proxy (the Docker network behind Caddy). Nothing else is trusted.
    public IList<string> KnownNetworks { get; init; } = [];

    public IList<string> KnownProxies { get; init; } = [];
}

public sealed class RateLimitSettings
{
    /// `public` policy (09 §5.15): token bucket, 120/min with a burst of 40.
    public TokenBucketSettings Public { get; set; } = new() { TokenLimit = 40, TokensPerPeriod = 2, ReplenishmentPeriodSeconds = 1 };
}

public sealed class TokenBucketSettings
{
    public int TokenLimit { get; set; }

    public int TokensPerPeriod { get; set; }

    public int ReplenishmentPeriodSeconds { get; set; }
}

public sealed class TelemetrySettings
{
    /// OTLP collector; empty = no export (09 §8.2).
    public string? OtlpEndpoint { get; set; }
}
