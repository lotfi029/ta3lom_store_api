using Ta3lom.Store.Application.Features.Outbox;

namespace Ta3lom.Store.Application.Features.Notifications;

/// RUL-63: render a `message_template` (ar fallback) and send it once per outbox message, channel and recipient.
internal sealed class NotificationDispatch(
    INotificationRepository notifications,
    ITemplateRenderer templates,
    ISmsSender sms,
    IEmailSender email,
    IUnitOfWork uow,
    IClock clock)
{
    public async Task SendOnceAsync(OutboxEnvelope envelope, MessageChannel channel, string recipient, string templateKey, string locale,
        object model, Guid? relatedOrderId, CancellationToken ct, IReadOnlyList<EmailAttachment>? attachments = null, string? storedRecipient = null)
    {
        if (channel == MessageChannel.Sms ? !SaudiMobile.IsValid(recipient) : !recipient.Contains('@', StringComparison.Ordinal))
        {
            return;                                                                         // e.g. "محذوف / deleted" after an account deletion
        }

        // `storedRecipient`: what `notification.recipient` keeps when the address itself is protected (beneficiary mobiles, PDPL).
        var row = await notifications.TryClaimAsync(envelope.Id, channel, storedRecipient ?? recipient, templateKey, locale, relatedOrderId, clock.UtcNow, ct);
        if (row is null)
        {
            return;                                                                     // already sent or in flight
        }

        var message = await templates.RenderAsync(templateKey, channel, locale, model, ct);
        var result = channel == MessageChannel.Sms
            ? ToDelivery(await sms.SendAsync(recipient, message.Body, ct))
            : await email.SendAsync(new EmailMessage(recipient, message.Subject ?? templateKey, message.Body, attachments ?? []), ct);
        if (result.Sent)
        {
            row.MarkSent(result.ProviderMessageId, clock.UtcNow);
        }
        else
        {
            row.MarkFailed(result.Error ?? "provider error", clock.UtcNow);
        }

        await uow.SaveChangesAsync(ct);
        if (!result.Sent)
        {
            throw new OutboxDeliveryException($"{channel.ToWire()} {templateKey} not delivered: {result.Error}");
        }
    }

    private static DeliveryResult ToDelivery(SmsSendResult r) => new(r.Sent, r.ProviderMessageId, r.Error);
}
