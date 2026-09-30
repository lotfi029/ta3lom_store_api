namespace Ta3lom.Store.Application.Interfaces;

/// Outgoing e-mail (09 §4.3 fakes table): `SinkEmailSender` (`.eml` files in `artifacts/mail`, `GET /dev/mail`) outside
/// Production, `SmtpEmailSender` (MailKit) when `Store:Email:Mode = smtp`.
public interface IEmailSender
{
    /// The staff alert mailbox (`Store:Email:StaffAlerts`).
    string StaffAlertsAddress { get; }

    /// The manager's mailbox for escalations (`Store:Email:ManagerAlerts`; the staff mailbox when empty, RUL-49).
    string ManagerAlertsAddress { get; }

    Task<DeliveryResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed record EmailMessage(string To, string Subject, string Body, IReadOnlyList<EmailAttachment> Attachments);

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// Provider outcome shared by SMS and e-mail.
public sealed record DeliveryResult(bool Sent, string? ProviderMessageId, string? Error)
{
    public static DeliveryResult Ok(string? providerMessageId) => new(true, providerMessageId, null);

    public static DeliveryResult Failed(string error) => new(false, null, error);
}

/// Development: the e-mails the sink wrote, newest first.
public interface IDevMailInbox
{
    Task<IReadOnlyList<DevMailMessage>> LatestAsync(int count, CancellationToken ct = default);
}

public sealed record DevMailMessage(DateTimeOffset SentAt, string To, string Subject, string Body, IReadOnlyList<string> Attachments, string File);
