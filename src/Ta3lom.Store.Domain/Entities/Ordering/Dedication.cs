namespace Ta3lom.Store.Domain.Entities.Ordering;

/// Table `dedication` (RUL-21, RUL-22): snapshot of a gift line's dedication with its public card token.
public sealed class Dedication : Entity
{
    public Dedication(Guid orderLineId, string toName, string toMobile, string templateKey, string cardToken) : base(Guid.CreateVersion7())
    {
        OrderLineId = Guard.NotEmpty(orderLineId);
        ToName = Guard.NotNull(toName);
        ToMobile = Guard.NotNull(toMobile);
        TemplateKey = Guard.NotNull(templateKey);
        CardToken = Guard.NotNull(cardToken);
        SmsStatus = DedicationSmsStatus.Pending;
    }

    private Dedication()
    {
    }

    public Guid OrderLineId { get; private set; }

    public string ToName { get; private set; } = null!;

    public string ToMobile { get; private set; } = null!;

    public string? FromName { get; private set; }

    public string TemplateKey { get; private set; } = null!;

    public string? CustomText { get; private set; }

    public string CardToken { get; private set; } = null!;

    public DedicationSmsStatus SmsStatus { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    /// RUL-22: the card SMS left (at most once per line; a staff resend is explicit).
    public void MarkSent(DateTimeOffset now)
    {
        SmsStatus = DedicationSmsStatus.Sent;
        SentAt = now;
    }

    public void MarkFailed() => SmsStatus = DedicationSmsStatus.Failed;

    /// #93: staff resend, optionally to a corrected mobile (E.164); the card goes out again.
    public void PrepareResend(string? correctedMobileE164)
    {
        if (correctedMobileE164 is not null)
        {
            ToMobile = correctedMobileE164;
        }

        SmsStatus = DedicationSmsStatus.Pending;
    }

    /// Template key of a free-text message (`dedication.template_key` is NOT NULL).
    public const string CustomTemplateKey = "custom";

    internal static Dedication ForLine(Guid orderLineId, CartDedication d) =>
        new(orderLineId, d.ToName, d.ToMobile, d.TemplateKey ?? CustomTemplateKey, NewCardToken())
        {
            FromName = string.IsNullOrWhiteSpace(d.FromName) ? null : d.FromName,
            CustomText = d.Text,
        };

    /// 22-character url-safe random token (16 random bytes, base64url without padding).
    private static string NewCardToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
