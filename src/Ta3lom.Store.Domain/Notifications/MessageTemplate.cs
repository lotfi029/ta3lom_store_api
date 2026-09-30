using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Domain.Notifications;

/// Table `message_template` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class MessageTemplate
{
    public MessageTemplate(string key, MessageChannel channel, string locale, string body, DateTimeOffset now)
    {
        Key = Guard.NotNull(key);
        Channel = channel;
        Locale = Guard.NotNull(locale);
        Body = Guard.NotNull(body);
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
}
