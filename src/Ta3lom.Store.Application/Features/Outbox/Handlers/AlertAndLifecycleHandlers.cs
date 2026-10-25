using System.Globalization;
using Ta3lom.Store.Application.Features.Notifications;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Outbox.Handlers;

/// Staff e-mail `staff_alert` (integrity checks, dead letters, OTP volume), once per message.
internal sealed class StaffAlertHandler(NotificationDispatch dispatch, IEmailSender email) : OutboxHandler<StaffAlert>
{
    protected override Task HandleAsync(StaffAlert message, OutboxEnvelope envelope, CancellationToken ct) =>
        dispatch.SendOnceAsync(envelope, MessageChannel.Email, email.StaffAlertsAddress, "staff_alert", "ar",
            new
            {
                subject = string.Create(CultureInfo.InvariantCulture, $"{message.Kind} {message.Reference}").Trim(),
                body = string.Create(CultureInfo.InvariantCulture,
                    $"kind: {message.Kind}\nreference: {message.Reference ?? "-"}\ndetail: {message.Detail}\nat: {envelope.OccurredAt:O}"),
            },
            ct,
            alertOnUnknown: message.Kind != NotificationDispatch.UnknownOutcomeKind,
            subject: string.Create(CultureInfo.InvariantCulture, $"kind {message.Kind}, reference {message.Reference ?? "-"}"));
}
