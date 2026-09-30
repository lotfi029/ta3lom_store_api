using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.ValueObjects;

namespace Ta3lom.Store.Infrastructure.Services.Sms;

/// Development/test SMS sink (plan 14 §4.3): keeps the last 200 messages in memory for `GET /dev/sms`.
/// The log line carries only the masked mobile and the length, never the body (OTP codes, 09 §8.1).
internal sealed partial class SinkSmsSender(IClock clock, ILogger<SinkSmsSender> logger) : ISmsSender, IDevSmsInbox
{
    private const int Capacity = 200;
    private readonly ConcurrentQueue<DevSmsMessage> _messages = new();

    public Task<SmsSendResult> SendAsync(string mobileE164, string body, CancellationToken ct = default)
    {
        _messages.Enqueue(new DevSmsMessage(clock.UtcNow, mobileE164, body));
        while (_messages.Count > Capacity)
        {
            _messages.TryDequeue(out _);
        }

        var masked = SaudiMobile.Mask(mobileE164);
        LogSent(logger, masked, body.Length);
        return Task.FromResult(SmsSendResult.Ok("sink-" + Guid.CreateVersion7().ToString("N")));
    }

    public IReadOnlyList<DevSmsMessage> Latest(int count) => [.. _messages.Reverse().Take(count)];

    [LoggerMessage(Level = LogLevel.Information, Message = "SMS sink: message to {Mobile} ({Length} chars)")]
    private static partial void LogSent(ILogger logger, string mobile, int length);
}
