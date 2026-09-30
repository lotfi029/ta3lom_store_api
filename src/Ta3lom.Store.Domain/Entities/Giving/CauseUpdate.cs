namespace Ta3lom.Store.Domain.Entities.Giving;

/// Table `cause_update` (RUL-54): a delivery update on a cause; `notify_donors` sends `CauseUpdatePublished` once, after which
/// the flag is read-only and the update cannot be deleted.
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

    public bool CanDelete => NotifiedAt is null;

    public static CauseUpdate Post(Guid productId, CauseUpdateDetails d, string createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(d);
        var u = new CauseUpdate(productId, d.TitleAr, d.OccurredOn, createdBy, now);
        u.Edit(d, now);
        return u;
    }

    /// Edits the content; `notifyDonors` true queues `CauseUpdatePublished` once (RUL-54) and is read-only afterwards.
    public void Edit(CauseUpdateDetails d, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(d);
        TitleAr = Guard.NotNull(d.TitleAr);
        TitleEn = d.TitleEn;
        BodyAr = d.BodyAr;
        BodyEn = d.BodyEn;
        AssetId = d.AssetId;
        UnitsDelivered = d.UnitsDelivered;
        OccurredOn = d.OccurredOn;
        IsPublic = d.IsPublic;
        if (NotifiedAt is null && d.NotifyDonors)
        {
            NotifyDonors = true;
            NotifiedAt = now;
            Raise(new Events.CauseUpdatePublished(Id, ProductId));
        }
    }
}

public sealed record CauseUpdateDetails(string TitleAr, string? TitleEn, string? BodyAr, string? BodyEn, Guid? AssetId, int? UnitsDelivered,
    DateOnly OccurredOn, bool IsPublic, bool NotifyDonors);
