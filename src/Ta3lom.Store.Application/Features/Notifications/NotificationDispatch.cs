using Ta3lom.Store.Application.Features.Outbox;

namespace Ta3lom.Store.Application.Features.Notifications;

/// Renders a `message_template` (Arabic fallback) and sends it at most once per outbox message, channel and recipient.
internal sealed class NotificationDispatch(
    INotificationRepository notifications,
    ITemplateRenderer templates,
    ISmsSender sms,
    IEmailSender email,
    IPseudonymizer pseudonymizer,
    IUnitOfWork uow,
    IClock clock)
{
    public async Task SendOnceAsync(OutboxEnvelope envelope, MessageChannel channel, string recipient, string templateKey, string locale,
        object model, CancellationToken ct)
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

        var dedupeKey = $"{envelope.Id:N}:{channel.ToWire()}:{recipientHash}";
        var row = await notifications.TryClaimAsync(dedupeKey, channel, recipientHash, templateKey, clock.UtcNow, ct);
        if (row is null)
        {
            return;                                                                      // already sent
        }

        var message = await templates.RenderAsync(templateKey, channel, locale, model, ct);
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
