using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Domain.Ordering;

/// Table `dedication` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
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
}
