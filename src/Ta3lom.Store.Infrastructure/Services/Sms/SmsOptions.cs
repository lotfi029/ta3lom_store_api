namespace Ta3lom.Store.Infrastructure.Services.Sms;

/// `Store:Sms` (09 §4.3). `Provider = sink` writes to the log and `/dev/sms`; `unifonic` calls the provider.
/// Keys are empty in the repo: the real provider needs the approved sender id (launch checklist).
public sealed class SmsOptions
{
    public const string Section = "Store:Sms";

    public string Provider { get; set; } = "sink";

    public string BaseUrl { get; set; } = "https://el.cloud.unifonic.com";

    public string AppSid { get; set; } = string.Empty;

    public string SenderId { get; set; } = string.Empty;
}
