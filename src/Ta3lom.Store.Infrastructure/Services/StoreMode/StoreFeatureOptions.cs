using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Infrastructure.Payments;

namespace Ta3lom.Store.Infrastructure.Services.StoreMode;

/// `Store:Features` (showcase mode). `CustomerSignIn = false` switches the donor OTP routes off (409
/// `store.signin_disabled`) and lifts the Production SMS requirement; staff sign-in is untouched.
public sealed class StoreFeatureOptions
{
    public const string Section = "Store:Features";

    public bool CustomerSignIn { get; set; } = true;
}

/// Checkout needs a signed-in donor: donor sign-in may only be off while payments are off (`Provider = none`).
/// Sign-in on with payments off is allowed (a donor can still sign in and see an empty account).
internal sealed class StoreFeatureOptionsValidator(IOptions<PaymentOptions> payments) : IValidateOptions<StoreFeatureOptions>
{
    public ValidateOptionsResult Validate(string? name, StoreFeatureOptions options) =>
        !options.CustomerSignIn && payments.Value.PaymentsEnabled
            ? ValidateOptionsResult.Fail("Store:Features:CustomerSignIn = false requires Store:Payments:Provider = none (checkout needs a signed-in donor)")
            : ValidateOptionsResult.Success;
}

/// The Application's view of the configuration (fixed for the life of the process).
internal sealed class ConfiguredStoreMode(IOptions<PaymentOptions> payments, IOptions<StoreFeatureOptions> features) : IStoreMode
{
    public bool PaymentsEnabled => payments.Value.PaymentsEnabled;

    public bool CustomerSignIn => features.Value.CustomerSignIn;
}

/// `/health/ready` → check `mode` with `data.mode` = `showcase` | `live` (always Healthy: the mode is a fact, not a fault).
internal sealed class StoreModeHealthCheck(IStoreMode mode) : IHealthCheck
{
    public const string Name = "mode";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(HealthCheckResult.Healthy(data: new Dictionary<string, object>
        {
            ["mode"] = mode.Mode,
            ["customerSignIn"] = mode.CustomerSignIn,
        }));
}

/// One warning at start-up when the store runs without payments, so the deploy log says so.
internal sealed partial class ShowcaseModeNotice(IStoreMode mode, ILogger<ShowcaseModeNotice> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!mode.PaymentsEnabled)
        {
            PaymentsDisabled(logger, mode.CustomerSignIn);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Store: payments disabled (showcase) — Store:Payments:Provider = none; donor sign-in {CustomerSignIn}")]
    private static partial void PaymentsDisabled(ILogger logger, bool customerSignIn);
}
