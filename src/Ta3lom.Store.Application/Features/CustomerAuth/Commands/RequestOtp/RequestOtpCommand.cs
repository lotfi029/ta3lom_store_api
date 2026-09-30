using System.Globalization;
using System.Security.Cryptography;
using Ta3lom.Store.Application.DTOs.CustomerAuth;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Domain.Entities.Notifications;

namespace Ta3lom.Store.Application.Features.CustomerAuth.Commands.RequestOtp;

/// RUL-60: 6-digit code, 5 minutes, 3 per mobile per 15 min, 10 per IP per hour. The plain code is only in
/// the SMS: it is sent directly (not via the outbox, RUL-63 exception) so it is never persisted or logged.
public sealed record RequestOtpCommand(string Mobile, string? Locale) : ICommand<OtpRequestedResponse>;

internal sealed class RequestOtpCommandHandler(
    IOtpChallengeRepository otps,
    INotificationRepository notifications,
    IOtpHasher hasher,
    ITemplateRenderer templates,
    ISmsSender sms,
    ICurrentActor actor,
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

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var id = Guid.CreateVersion7();
        otps.Add(OtpChallenge.Issue(id, mobile, OtpPurpose.Login, hasher.Hash(id, mobile, code), actor.IpHash, now + SessionPolicy.OtpLifetime, now));
        await uow.SaveChangesAsync(ct);                                      // committed before the SMS goes out

        var locale = command.Locale is "en" ? "en" : "ar";
        var message = await templates.RenderAsync("otp", MessageChannel.Sms, locale, new { code }, ct);
        var sent = await sms.SendAsync(mobile, message.Body, ct);

        var log = Notification.Direct(MessageChannel.Sms, mobile, "otp", locale, relatedOrderId: null, now);
        if (sent.Sent)
        {
            log.MarkSent(sent.ProviderMessageId, clock.UtcNow);
        }
        else
        {
            log.MarkFailed(sent.Error ?? "send failed", clock.UtcNow);
        }

        notifications.Add(log);
        await uow.SaveChangesAsync(ct);

        return new OtpRequestedResponse((int)SessionPolicy.OtpLifetime.TotalSeconds);
    }
}
