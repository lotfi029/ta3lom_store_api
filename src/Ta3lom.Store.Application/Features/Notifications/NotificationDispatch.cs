using System.Globalization;
using Ta3lom.Store.Application.Features.Outbox;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Notifications;

/// Renders a `message_template` (Arabic fallback) and sends it at most once per outbox message, channel and recipient.
/// A send whose outcome is unknown (the row is still `sending` when the message comes back: a crash, a lost reply, a
/// handler past its lease) is never repeated: the row becomes `unknown` and staff get one alert (A-2).
internal sealed class NotificationDispatch(
    INotificationRepository notifications,
    ITemplateRenderer templates,
    ISmsSender sms,
    IEmailSender email,
    IPseudonymizer pseudonymizer,
    IOutbox outbox,
    IUnitOfWork uow,
    IClock clock)
{
    public const string UnknownOutcomeKind = "notification_unknown";

    /// `alertOnUnknown` is false for the unknown-outcome alert itself, so a lost alert does not alert again; `subject` names
    /// what the message was about (R12-5: e.g. the original alert's kind and reference) in that alert.
    public async Task SendOnceAsync(OutboxEnvelope envelope, MessageChannel channel, string recipient, string templateKey, string locale,
        object model, CancellationToken ct, bool alertOnUnknown = true, string? subject = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        string recipientHash;
        if (channel == MessageChannel.Sms)
        {
            if (!SaudiMobile.TryNormalize(recipient, out var mobile))
            {
                return;                                                                  // e.g. an anonymised donor
            }

            recipient = mobile;
            recipientHash = pseudonymizer.HashMobile(mobile);
        }
        else
        {
            if (!recipient.Contains('@', StringComparison.Ordinal))
            {
                return;
            }

            recipientHash = pseudonymizer.HashEmail(recipient);
        }

        var message = await templates.RenderAsync(templateKey, channel, locale, model, ct);       // before the claim: a render error is retried
        var dedupeKey = $"{envelope.Id:N}:{channel.ToWire()}:{recipientHash}";
        var (row, claimed) = await notifications.TryClaimAsync(dedupeKey, channel, recipientHash, templateKey, clock.UtcNow, ct);
        if (!claimed)
        {
            if (row.Status == NotificationStatus.Sending)
            {
                row.MarkUnknown();
                if (alertOnUnknown)
                {
                    outbox.Enqueue(new StaffAlert(UnknownOutcomeKind, null, string.Create(CultureInfo.InvariantCulture,
                        $"{templateKey} ({channel.ToWire()}) of outbox message {envelope.Id}{(subject is null ? string.Empty : $" [{subject}]")}: delivery outcome unknown, not sent again")));
                }

                await uow.SaveChangesAsync(ct);
            }

            return;                                                                      // sent, or never again
        }

        var result = channel == MessageChannel.Sms
            ? ToDelivery(await sms.SendAsync(recipient, message.Body, ct))
            : await email.SendAsync(new EmailMessage(recipient, message.Subject ?? templateKey, message.Body, []), ct);
        if (result.Sent)
        {
            row.MarkSent(result.ProviderMessageId);
        }
        else
        {
            row.MarkFailed();
        }

        await uow.SaveChangesAsync(ct);
        if (!result.Sent)
        {
            throw new OutboxDeliveryException($"{channel.ToWire()} {templateKey} not delivered: {result.Error}");
        }
    }

    private static DeliveryResult ToDelivery(SmsSendResult r) => new(r.Sent, r.ProviderMessageId, r.Error);
}
