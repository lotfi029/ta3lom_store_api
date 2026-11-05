using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ta3lom.Store.Infrastructure.Payments;

/// `Store:Payments` (F-D1). `Provider` picks the adapter for NEW payments; every adapter stays registered so webhooks
/// and reconciliation still reach payments taken by the other one. Keys are never in the repository: secret files on
/// the server (deploy/secrets.md).
public sealed class PaymentOptions
{
    public const string Section = "Store:Payments";

    /// The provider that switches payments off (showcase mode): no default gateway, no payment jobs, 409 on the payment routes.
    public const string None = "none";

    /// "moyasar" | "fake" (the fake only in Development and the test host — C-2) | "none" (showcase, every environment).
    public string Provider { get; set; } = "fake";

    /// False when `Provider = none` (showcase mode).
    public bool PaymentsEnabled => !IsNone(Provider);

    public static bool IsNone(string? provider) => string.Equals(provider?.Trim(), None, StringComparison.OrdinalIgnoreCase);

    public MoyasarOptions Moyasar { get; set; } = new();

    public FakePaymentOptions Fake { get; set; } = new();
}

public sealed class MoyasarOptions
{
    public string BaseUrl { get; set; } = "https://api.moyasar.com/v1/";

    /// `sk_live_…` in Production, `sk_test_…` everywhere else.
    public string SecretKey { get; set; } = string.Empty;

    /// The webhook's shared secret (the `secret_token` Moyasar puts in every webhook body).
    public string WebhookSecret { get; set; } = string.Empty;

    /// Seconds per call (the checkout waits for the hosted page).
    public int TimeoutSeconds { get; set; } = 15;

    public bool IsLiveKey => SecretKey.StartsWith("sk_live_", StringComparison.Ordinal);
}

public sealed class FakePaymentOptions
{
    /// The fake hosted page lives on this API: `{ApiBaseUrl}/dev/payments/{id}`. Defaults to `Store:PublicBaseUrl`.
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// Signs the fake webhooks. DEV ONLY default; the fake is refused outside Development and the test host anyway.
    public string WebhookSecret { get; set; } = DevOnlyWebhookSecret;

    public static readonly string DevOnlyWebhookSecret =
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("ta3lom-store DEV ONLY fake webhook")));
}

/// Showcase mode: `none` is accepted in every environment and validates nothing else.
/// C-2 / plan §3.2: the fake is refused outside Development and the test host ("Testing"); Moyasar needs its keys; the
/// key mode matches the environment (live keys only in Production, test keys elsewhere); no DEV ONLY value outside
/// Development.
internal sealed class PaymentOptionsValidator(IHostEnvironment environment) : IValidateOptions<PaymentOptions>
{
    public const string TestingEnvironment = "Testing";

    public ValidateOptionsResult Validate(string? name, PaymentOptions options)
    {
        List<string> problems = [];
        var provider = options.Provider?.Trim().ToLowerInvariant();
        var devOrTest = environment.IsDevelopment() || environment.IsEnvironment(TestingEnvironment);
        switch (provider)
        {
            case "fake":
                if (!devOrTest)
                {
                    problems.Add($"Store:Payments:Provider = fake is refused in {environment.EnvironmentName} (Development and tests only)");
                }

                break;
            case "moyasar":
                ValidateMoyasar(options.Moyasar, problems);
                break;
            case PaymentOptions.None:
                break;                                   // showcase: no gateway, so the Moyasar settings are not needed
            default:
                problems.Add("Store:Payments:Provider must be moyasar, fake or none");
                break;
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    private void ValidateMoyasar(MoyasarOptions m, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(m.SecretKey))
        {
            problems.Add("Store:Payments:Moyasar:SecretKey is required");
        }
        else if (environment.IsProduction() && !m.SecretKey.StartsWith("sk_live_", StringComparison.Ordinal))
        {
            problems.Add("Store:Payments:Moyasar:SecretKey must be a live key (sk_live_…) in Production");
        }
        else if (!environment.IsProduction() && !environment.IsEnvironment(TestingEnvironment) && !m.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal))
        {
            problems.Add($"Store:Payments:Moyasar:SecretKey must be a test key (sk_test_…) in {environment.EnvironmentName}");
        }

        if (string.IsNullOrWhiteSpace(m.WebhookSecret) || m.WebhookSecret.Length < 16)
        {
            problems.Add("Store:Payments:Moyasar:WebhookSecret is required (at least 16 characters)");
        }
        else if (m.WebhookSecret == FakePaymentOptions.DevOnlyWebhookSecret || m.WebhookSecret.Contains("DEV ONLY", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("Store:Payments:Moyasar:WebhookSecret must not be a DEV ONLY value");
        }

        if (!Uri.TryCreate(m.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            problems.Add("Store:Payments:Moyasar:BaseUrl must be an https URL");
        }

        if (m.TimeoutSeconds is < 2 or > 60)
        {
            problems.Add("Store:Payments:Moyasar:TimeoutSeconds must be between 2 and 60");
        }
    }
}
