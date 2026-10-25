namespace Ta3lom.Store.Domain.Entities.Notifications;

/// Table `message_template`: a Scriban template per key, channel and locale (the OTP SMS, staff alerts).
public sealed class MessageTemplate
{
    public MessageTemplate(string key, MessageChannel channel, string locale, string? subject, string body, DateTimeOffset now)
    {
        Key = Guard.NotEmpty(key);
        Channel = channel;
        Locale = locale is "ar" or "en" ? locale : throw new DomainException("template.locale_invalid");
        Subject = string.IsNullOrWhiteSpace(subject) ? null : subject;
        Body = Guard.NotEmpty(body);
        UpdatedAt = now;
    }

    private MessageTemplate()
    {
    }

    public string Key { get; private set; } = null!;

    public MessageChannel Channel { get; private set; }

    public string Locale { get; private set; } = null!;

    public string? Subject { get; private set; }

    public string Body { get; private set; } = null!;

    public DateTimeOffset UpdatedAt { get; private set; }

    public string? UpdatedBy { get; private set; }

    /// Staff edit; the caller has checked that the text parses as Scriban.
    public void Edit(string? subject, string body, string? updatedBy, DateTimeOffset now)
    {
        Subject = string.IsNullOrWhiteSpace(subject) ? null : subject;
        Body = Guard.NotEmpty(body);
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
