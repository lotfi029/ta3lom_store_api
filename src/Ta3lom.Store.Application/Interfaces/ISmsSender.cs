namespace Ta3lom.Store.Application.Interfaces;

/// Outgoing SMS: `SinkSmsSender` in Development/Staging/tests, a provider adapter in
/// Production. Implementations never log the body (it may carry an OTP code).
public interface ISmsSender
{
    Task<SmsSendResult> SendAsync(string mobileE164, string body, CancellationToken ct = default);
}

public sealed record SmsSendResult(bool Sent, string? ProviderMessageId, string? Error)
{
    public static SmsSendResult Ok(string? providerMessageId) => new(true, providerMessageId, null);

    public static SmsSendResult Failed(string error) => new(false, null, error);
}

/// The development SMS sink's inbox (`GET /dev/sms`).
public interface IDevSmsInbox
{
    IReadOnlyList<DevSmsMessage> Latest(int count);
}

public sealed record DevSmsMessage(DateTimeOffset SentAt, string To, string Body);
