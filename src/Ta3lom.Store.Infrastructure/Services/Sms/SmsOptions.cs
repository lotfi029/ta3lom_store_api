using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ta3lom.Store.Infrastructure.Services.Sms;

/// `Store:Sms`. `Provider = sink` writes to the log and `/dev/sms`; `unifonic` calls the provider.
/// Keys are empty in the repo: the real provider needs the approved sender id (go-live checklist in deploy/README.md).
public sealed class SmsOptions
{
    public const string Section = "Store:Sms";

    public string Provider { get; set; } = "sink";

    public string BaseUrl { get; set; } = "https://el.cloud.unifonic.com";

    public string AppSid { get; set; } = string.Empty;

    public string SenderId { get; set; } = string.Empty;
}

/// A-8: Production sends real SMS — the provider must be `unifonic` with its AppSid and sender id, or the host does not start
/// (an OTP would otherwise be accepted and silently never delivered).
internal sealed class SmsOptionsValidator(IHostEnvironment environment) : IValidateOptions<SmsOptions>
{
    public ValidateOptionsResult Validate(string? name, SmsOptions options)
    {
        if (!environment.IsProduction())
        {
            return ValidateOptionsResult.Success;
        }

        List<string> problems = [];
        if (!string.Equals(options.Provider, "unifonic", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("Store:Sms:Provider must be unifonic in Production");
        }

        if (string.IsNullOrWhiteSpace(options.AppSid))
        {
            problems.Add("Store:Sms:AppSid is required in Production");
        }

        if (string.IsNullOrWhiteSpace(options.SenderId))
        {
            problems.Add("Store:Sms:SenderId is required in Production");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
