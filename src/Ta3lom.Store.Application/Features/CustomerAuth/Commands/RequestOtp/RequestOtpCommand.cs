using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.CustomerAuth;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Domain.Entities.Notifications;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.CustomerAuth.Commands.RequestOtp;

/// RUL-60: 6-digit code, 5 minutes, 3 per mobile per 15 min, 10 per IP per hour; 08 §6.2: at most
/// `OtpDailyGlobalCap` codes store-wide per Riyadh day (SMS pumping), with one staff alert when the cap is hit. The plain
/// code is only in the SMS: it is sent directly (not via the outbox, RUL-63 exception) so it is never persisted or logged.
public sealed record RequestOtpCommand(string Mobile, string? Locale) : ICommand<OtpRequestedResponse>;

internal sealed class RequestOtpCommandHandler(
    IOtpChallengeRepository otps,
    INotificationRepository notifications,
    IOtpHasher hasher,
    ITemplateRenderer templates,
    ISmsSender sms,
    IPseudonymizer pseudonymizer,
    ICurrentActor actor,
    IApplicationDbContext db,
    IOutbox outbox,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<RequestOtpCommand, OtpRequestedResponse>
{
    public async Task<Result<OtpRequestedResponse>> HandleAsync(RequestOtpCommand command, CancellationToken ct = default)
    {
        if (!SaudiMobile.TryNormalize(command.Mobile, out var mobile))
        {
            return OtpErrors.MobileInvalid;
        }

        var now = clock.UtcNow;
        var byMobile = await otps.IssuedForMobileSinceAsync(mobile, now - SessionPolicy.OtpMobileWindow, ct);
        if (byMobile.Count >= SessionPolicy.OtpMaxPerMobile)
        {
            return OtpErrors.RateLimited(byMobile[0] + SessionPolicy.OtpMobileWindow - now);
        }

        if (actor.IpHash is { } ipHash)
        {
            var byIp = await otps.IssuedForIpSinceAsync(ipHash, now - SessionPolicy.OtpIpWindow, ct);
            if (byIp.Count >= SessionPolicy.OtpMaxPerIp)
            {
                return OtpErrors.RateLimited(byIp[0] + SessionPolicy.OtpIpWindow - now);
            }
        }

        var local = now.ToOffset(Riyadh);
        var dayStart = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, Riyadh).ToUniversalTime();
        if (await otps.CountIssuedSinceAsync(dayStart, ct) >= SessionPolicy.OtpDailyGlobalCap)
        {
            await AlertCapOnceAsync(dayStart, ct);
            return OtpErrors.RateLimited(dayStart.AddDays(1) - now);
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var id = Guid.CreateVersion7();
        otps.Add(OtpChallenge.Issue(id, mobile, OtpPurpose.Login, hasher.Hash(id, mobile, code), actor.IpHash, now + SessionPolicy.OtpLifetime, now));
        await uow.SaveChangesAsync(ct);                                      // committed before the SMS goes out

        var locale = command.Locale is "en" ? "en" : "ar";
        var message = await templates.RenderAsync("otp", MessageChannel.Sms, locale, new { code }, ct);
        var sent = await sms.SendAsync(mobile, message.Body, ct);

        var log = new Notification(MessageChannel.Sms, pseudonymizer.HashMobile(mobile), "otp", null, now);
        if (sent.Sent)
        {
            log.MarkSent(sent.ProviderMessageId);
        }
        else
        {
            log.MarkFailed();
        }

        notifications.Add(log);
        await uow.SaveChangesAsync(ct);

        return new OtpRequestedResponse((int)SessionPolicy.OtpLifetime.TotalSeconds);
    }

    public const string CapAlertKind = "otp_daily_cap";

    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);

    private async Task AlertCapOnceAsync(DateTimeOffset dayStart, CancellationToken ct)
    {
        var alerts = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == nameof(StaffAlert) && m.OccurredAt >= dayStart).Select(m => m.Payload).ToListAsync(ct);   // jsonb: filtered here
        if (!alerts.Exists(p => p.Contains(CapAlertKind, StringComparison.Ordinal)))
        {
            outbox.Enqueue(new StaffAlert(CapAlertKind, null,
                string.Create(CultureInfo.InvariantCulture, $"OTP daily cap of {SessionPolicy.OtpDailyGlobalCap} codes reached; requests refused until midnight Riyadh")));
            await uow.SaveChangesAsync(ct);
        }
    }
}
