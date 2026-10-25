using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.ValueObjects;

namespace Ta3lom.Store.Infrastructure.Services.Sms;

/// Real SMS provider behind `Store:Sms:Provider = unifonic`. Not used until the owner supplies the
/// AppSid and the approved sender id; the keys stay empty in the repository (REVIEW in DECISIONS.md).
internal sealed partial class UnifonicSmsSender(HttpClient http, IOptions<SmsOptions> options, ILogger<UnifonicSmsSender> logger) : ISmsSender
{
    public async Task<SmsSendResult> SendAsync(string mobileE164, string body, CancellationToken ct = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.AppSid) || string.IsNullOrWhiteSpace(o.SenderId))
        {
            return SmsSendResult.Failed("sms provider not configured");
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["AppSid"] = o.AppSid,
            ["SenderID"] = o.SenderId,
            ["Recipient"] = mobileE164.TrimStart('+'),
            ["Body"] = body,
        });
        try
        {
            using var response = await http.PostAsync(new Uri(new Uri(o.BaseUrl), "/rest/SMS/messages"), content, ct);
            if (!response.IsSuccessStatusCode)
            {
                var masked = SaudiMobile.Mask(mobileE164);
                LogFailed(logger, masked, (int)response.StatusCode);
                return SmsSendResult.Failed($"provider status {(int)response.StatusCode}");
            }

            var payload = await response.Content.ReadFromJsonAsync<UnifonicResponse>(ct);
            return payload?.Success == true
                ? SmsSendResult.Ok(payload.Data?.MessageId)
                : SmsSendResult.Failed(payload?.ErrorCode ?? "provider error");
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))   // A-1: a timeout
        {
            var masked = SaudiMobile.Mask(mobileE164);
            LogFailed(logger, masked, 0);
            return SmsSendResult.Failed(ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "SMS provider failed for {Mobile} with status {Status}")]
    private static partial void LogFailed(ILogger logger, string mobile, int status);

    private sealed record UnifonicResponse(bool Success, string? ErrorCode, UnifonicData? Data);

    private sealed record UnifonicData(string? MessageId);
}
