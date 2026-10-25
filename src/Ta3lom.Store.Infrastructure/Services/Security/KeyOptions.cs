using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ta3lom.Store.Infrastructure.Services.Security;

/// Signing keys and peppers. Real values come from the environment (or files under /run/secrets).
/// In Development an empty key falls back to its DEV ONLY value; every other environment refuses to start when a key
/// is missing, equals its DEV ONLY value or is shorter than 32 bytes.
public sealed class JwtOptions
{
    public const string Section = "Store:Jwt";

    public string Issuer { get; set; } = "https://store-api.ta3lom.sa";

    public string CustomerAudience { get; set; } = "store-customer";

    public string StaffAudience { get; set; } = "store-staff";

    public string StaffMfaAudience { get; set; } = "store-staff-mfa";

    /// `kid` header; rotate by changing it together with the key.
    public string KeyId { get; set; } = "k1";

    public string CustomerSigningKeyBase64 { get; set; } = string.Empty;

    public string StaffSigningKeyBase64 { get; set; } = string.Empty;
}

public sealed class OtpOptions
{
    public const string Section = "Store:Otp";

    public string PepperBase64 { get; set; } = string.Empty;
}

public sealed class TokenKeyOptions
{
    public const string Section = "Store:Tokens";

    /// HMAC key of the per-order status token (`GET /v1/orders/{ref}/status?t=`).
    public string StatusKeyBase64 { get; set; } = string.Empty;
}

/// HMAC key for the pseudonymised values (IP hashes, recipient hashes). Changing it breaks lookups of existing hashes.
public sealed class HashingOptions
{
    public const string Section = "Store:Hashing";

    public string KeyBase64 { get; set; } = string.Empty;
}

/// Deterministic development keys: SHA-256("ta3lom-store DEV ONLY &lt;purpose&gt;"). Public by design, refused outside Development.
public static class DevOnlyKeys
{
    public const string JwtCustomer = "+zco5EjjaH++zZf/zVfF/52+4kgGxbIIYIXf3tlwPlY=";
    public const string JwtStaff = "yyp7JEZrsV3kfEczPvnNSyvU4ipbgz9ZO9VX/oul4j0=";
    public const string OtpPepper = "RmWH6Y2B4QC79EjwbHT9RREfbcIzuTpo095HTumzUtI=";
    public const string OrderStatus = "JRSbCH7+CypDBf48ErxfUj6JVHKluq1YTByYLNf8THs=";
    public const string Hashing = "dxjryIuC+ysWAAN6/KjHOgY+5AY52AUi6l2qO/VjyJM=";

    public static int KeyLength(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return 0;
        }

        try
        {
            return Convert.FromBase64String(base64).Length;
        }
        catch (FormatException)
        {
            return 0;
        }
    }

    private static readonly string[] All = [JwtCustomer, JwtStaff, OtpPepper, OrderStatus, Hashing];

    /// L-1: compared as key bytes (base64 decoding ignores whitespace) against every DEV ONLY key, not as a string.
    public static bool IsDevOnly(string? value)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value ?? string.Empty);
        }
        catch (FormatException)
        {
            return false;
        }

        return All.Any(dev => CryptographicOperations.FixedTimeEquals(bytes, Convert.FromBase64String(dev)));
    }

    /// R12-4: two base64 values that decode to the same bytes are the same key (whitespace and line breaks are ignored by
    /// the decoder), compared in fixed time.
    public static bool SameKey(string? a, string? b)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(a ?? string.Empty), Convert.FromBase64String(b ?? string.Empty));
        }
        catch (FormatException)
        {
            return string.Equals(a, b, StringComparison.Ordinal);
        }
    }

    /// Null when the key is acceptable for <paramref name="environment"/>, else the reason.
    public static string? Problem(string name, string? value, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDevelopment())
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return $"{name} is required outside Development";
            }

            if (IsDevOnly(value))
            {
                return $"{name} must not be a DEV ONLY value outside Development";
            }
        }

        return KeyLength(value) >= 32 ? null : $"{name} must be at least 32 bytes (base64)";
    }
}

/// Fills empty keys with their DEV ONLY value in Development (so it starts without secrets), then validates every key.
internal sealed class KeyOptionsSetup(IHostEnvironment environment)
    : IPostConfigureOptions<JwtOptions>, IPostConfigureOptions<OtpOptions>, IPostConfigureOptions<TokenKeyOptions>, IPostConfigureOptions<HashingOptions>,
      IValidateOptions<JwtOptions>, IValidateOptions<OtpOptions>, IValidateOptions<TokenKeyOptions>, IValidateOptions<HashingOptions>
{
    public void PostConfigure(string? name, JwtOptions options)
    {
        options.CustomerSigningKeyBase64 = Fallback(options.CustomerSigningKeyBase64, DevOnlyKeys.JwtCustomer);
        options.StaffSigningKeyBase64 = Fallback(options.StaffSigningKeyBase64, DevOnlyKeys.JwtStaff);
    }

    public void PostConfigure(string? name, OtpOptions options) => options.PepperBase64 = Fallback(options.PepperBase64, DevOnlyKeys.OtpPepper);

    public void PostConfigure(string? name, TokenKeyOptions options) => options.StatusKeyBase64 = Fallback(options.StatusKeyBase64, DevOnlyKeys.OrderStatus);

    public void PostConfigure(string? name, HashingOptions options) => options.KeyBase64 = Fallback(options.KeyBase64, DevOnlyKeys.Hashing);

    public ValidateOptionsResult Validate(string? name, JwtOptions options) => Result(
        DevOnlyKeys.Problem("Store:Jwt:CustomerSigningKeyBase64", options.CustomerSigningKeyBase64, environment),
        DevOnlyKeys.Problem("Store:Jwt:StaffSigningKeyBase64", options.StaffSigningKeyBase64, environment),
        DevOnlyKeys.SameKey(options.CustomerSigningKeyBase64, options.StaffSigningKeyBase64) && !environment.IsDevelopment()
            ? "Store:Jwt customer and staff keys must differ" : null);

    public ValidateOptionsResult Validate(string? name, OtpOptions options) =>
        Result(DevOnlyKeys.Problem("Store:Otp:PepperBase64", options.PepperBase64, environment));

    public ValidateOptionsResult Validate(string? name, TokenKeyOptions options) =>
        Result(DevOnlyKeys.Problem("Store:Tokens:StatusKeyBase64", options.StatusKeyBase64, environment));

    public ValidateOptionsResult Validate(string? name, HashingOptions options) =>
        Result(DevOnlyKeys.Problem("Store:Hashing:KeyBase64", options.KeyBase64, environment));

    private string Fallback(string value, string devValue) =>
        string.IsNullOrWhiteSpace(value) && environment.IsDevelopment() ? devValue : value;

    private static ValidateOptionsResult Result(params string?[] problems)
    {
        var failures = problems.Where(p => p is not null).Select(p => p!).ToList();
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
