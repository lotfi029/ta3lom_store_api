using System.Globalization;
using System.Security.Cryptography;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.CustomerAuth;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Domain.Entities.Notifications;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.CustomerAuth.Commands.RequestOtp;

/// D-10: a 6-digit code valid 5 minutes; at most 3 per mobile per 15 minutes and 10 per IP per hour, counted under an advisory
/// lock on the mobile so parallel requests cannot slip past; at most `OtpDailyGlobalCap` store-wide per Riyadh day (SMS
/// pumping) with one staff alert at half of it. The answer is the same whether the mobile has an account or not. The plain
/// code is only in the SMS, sent after the challenge commits, so it is never stored or logged.
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
    public const string VolumeAlertKind = "otp_volume_half_cap";

    public async Task<Result<OtpRequestedResponse>> HandleAsync(RequestOtpCommand command, CancellationToken ct = default)
    {
        if (!SaudiMobile.TryNormalize(command.Mobile, out var mobile))
        {
            return OtpErrors.MobileInvalid;
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var issued = await uow.ExecuteInTransactionAsync(token => IssueAsync(mobile, code, token), ct);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        var locale = command.Locale is "en" ? "en" : "ar";
        var message = await templates.RenderAsync("otp", MessageChannel.Sms, locale, new { code }, ct);
        var sent = await sms.SendAsync(mobile, message.Body, ct);
        var log = new Notification(MessageChannel.Sms, pseudonymizer.HashMobile(mobile), "otp", null, clock.UtcNow);
        if (sent.Sent)
        {
            log.MarkSent(sent.ProviderMessageId);
        }

        notifications.Add(log);
        await uow.SaveChangesAsync(ct);
        return new OtpRequestedResponse((int)SessionPolicy.OtpLifetime.TotalSeconds);
    }

    private async Task<Result> IssueAsync(string mobile, string code, CancellationToken ct)
    {
        await otps.LockMobileAsync(mobile, ct);
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

        var dayStart = RiyadhDayStart(now);
        var today = await otps.CountIssuedSinceAsync(dayStart, ct);
        if (today >= SessionPolicy.OtpDailyGlobalCap)
        {
            return OtpErrors.RateLimited(dayStart.AddDays(1) - now);
        }

        if (today + 1 >= SessionPolicy.OtpDailyGlobalCap / 2 && !await AlertedTodayAsync(dayStart, ct))
        {
            outbox.Enqueue(new StaffAlert(VolumeAlertKind, null, string.Create(CultureInfo.InvariantCulture,
                $"{today + 1} OTP codes today: half of the daily cap of {SessionPolicy.OtpDailyGlobalCap}. Above the cap requests are refused until midnight Riyadh.")));
        }

        var id = Guid.CreateVersion7();
        otps.Add(OtpChallenge.Issue(id, mobile, OtpPurpose.Login, hasher.Hash(id, mobile, code), actor.IpHash, now + SessionPolicy.OtpLifetime, now));
        return Result.Success();
    }

    /// One alert per Riyadh day: an earlier alert of today is in the outbox (rows live 30 days).
    private async Task<bool> AlertedTodayAsync(DateTimeOffset dayStart, CancellationToken ct)
    {
        var alerts = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == nameof(StaffAlert) && m.OccurredAt >= dayStart).Select(m => m.Payload).ToListAsync(ct);   // jsonb: filtered here
        return alerts.Exists(p => p.Contains(VolumeAlertKind, StringComparison.Ordinal));
    }

    private static DateTimeOffset RiyadhDayStart(DateTimeOffset now)
    {
        var riyadh = TimeSpan.FromHours(3);
        var local = now.ToOffset(riyadh);
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, riyadh).ToUniversalTime();
    }
}

/// Shape only; an invalid mobile is the business code `otp.mobile_invalid`, returned by the handler.
internal sealed class RequestOtpCommandValidator : AbstractValidator<RequestOtpCommand>
{
    public RequestOtpCommandValidator()
    {
        RuleFor(c => c.Mobile).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Locale).Must(l => l is null or "ar" or "en").WithMessage("locale must be 'ar' or 'en'.");
    }
}
