using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.Extensions.Options;

namespace Ta3lom.Store.Api.Options;

/// The `Store` configuration section. Validated at start-up: the app refuses to run misconfigured.
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

    /// `Store:Otp` (the pepper is bound by Infrastructure); only `Development` is read here (API-8).
    public OtpSettings Otp { get; set; } = new();

    /// Returns every problem found for <paramref name="environment"/>, or an empty list (used by ValidateOnStart).
    /// API-4: Staging and Production accept https origins only, and Production never the staging site.
    public IReadOnlyList<string> Validate(string environment)
    {
        var strict = environment is "Production" or "Staging";
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
            else if (strict && uri.Scheme != Uri.UriSchemeHttps)
            {
                problems.Add($"Store:Cors:Origins entry '{origin}' must be https in {environment}");
            }

            if (environment == "Production" && origin.Contains("staging", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"Store:Cors:Origins entry '{origin}' is a staging origin; Production accepts the store site only");
            }
        }

        foreach (var network in ForwardedHeaders.KnownNetworks)
        {
            if (!IPNetwork.TryParse(network, out _))
            {
                problems.Add($"Store:ForwardedHeaders:KnownNetworks entry '{network}' is not a CIDR network");
            }
        }

        // API-8: the raised OTP caps exist for local runs only.
        var dev = Otp.Development;
        foreach (var (key, value) in new[] { ("Store:Otp:Development:MaxPerMobile", dev.MaxPerMobile), ("Store:Otp:Development:MaxPerIp", dev.MaxPerIp) })
        {
            if (value is null)
            {
                continue;
            }

            if (environment != "Development")
            {
                problems.Add($"{key} is a Development-only setting; remove it in {environment}");
            }
            else if (value < 1)
            {
                problems.Add($"{key} must be at least 1");
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
    /// Allowed browser origins: per environment in appsettings.{Environment}.json.
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
    /// `public` policy: token bucket, 120/min with a burst of 40.
    public TokenBucketSettings Public { get; set; } = new() { TokenLimit = 40, TokensPerPeriod = 2, ReplenishmentPeriodSeconds = 1 };

    /// Window policies (per hashed IP, or per subject where noted).
    public WindowSettings OtpRequest { get; set; } = new() { PermitLimit = 10, WindowSeconds = 3600 };

    public WindowSettings OtpVerify { get; set; } = new() { PermitLimit = 30, WindowSeconds = 900 };

    public WindowSettings AuthRefresh { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };

    public WindowSettings StaffLogin { get; set; } = new() { PermitLimit = 10, WindowSeconds = 900 };

    public WindowSettings Cart { get; set; } = new() { PermitLimit = 60, WindowSeconds = 60 };

    public WindowSettings Checkout { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    public WindowSettings Me { get; set; } = new() { PermitLimit = 120, WindowSeconds = 60 };

    public WindowSettings Download { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    public WindowSettings Admin { get; set; } = new() { PermitLimit = 300, WindowSeconds = 60 };

    /// D-b: 5 template test sends per staff member per hour.
    public WindowSettings TemplateTest { get; set; } = new() { PermitLimit = 5, WindowSeconds = 3600 };
}

public sealed class WindowSettings
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }
}

public sealed class TokenBucketSettings
{
    public int TokenLimit { get; set; }

    public int TokensPerPeriod { get; set; }

    public int ReplenishmentPeriodSeconds { get; set; }
}

public sealed class TelemetrySettings
{
    /// OTLP collector; empty = no export.
    public string? OtlpEndpoint { get; set; }
}

/// Runs <see cref="StoreOptions.Validate(string)"/> for the host environment and reports every problem.
internal sealed class StoreOptionsValidator(IHostEnvironment environment) : IValidateOptions<StoreOptions>
{
    public ValidateOptionsResult Validate(string? name, StoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = options.Validate(environment.EnvironmentName);
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}

public sealed class OtpSettings
{
    public OtpDevelopmentSettings Development { get; set; } = new();
}

/// `Store:Otp:Development` (Development only): raised D-10 caps for local end-to-end runs; absent = the fixed caps.
public sealed class OtpDevelopmentSettings
{
    public int? MaxPerMobile { get; set; }

    public int? MaxPerIp { get; set; }
}
