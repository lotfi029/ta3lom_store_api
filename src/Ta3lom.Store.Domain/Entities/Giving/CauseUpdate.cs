namespace Ta3lom.Store.Domain.Entities.Giving;

/// Table `cause_update` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class CauseUpdate : AggregateRoot
{
    public CauseUpdate(Guid productId, string titleAr, DateOnly occurredOn, string createdBy, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        ProductId = Guard.NotEmpty(productId);
        TitleAr = Guard.NotNull(titleAr);
        OccurredOn = occurredOn;
        IsPublic = true;
        NotifyDonors = false;
        CreatedAt = now;
        CreatedBy = Guard.NotNull(createdBy);
    }

    private CauseUpdate()
    {
    }

    public Guid ProductId { get; private set; }

    public string TitleAr { get; private set; } = null!;

    public string? TitleEn { get; private set; }

    public string? BodyAr { get; private set; }

    public string? BodyEn { get; private set; }

    public Guid? AssetId { get; private set; }

    public int? UnitsDelivered { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public bool IsPublic { get; private set; }

    public bool NotifyDonors { get; private set; }

    public DateTimeOffset? NotifiedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string CreatedBy { get; private set; } = null!;
}
