namespace Ta3lom.Store.Domain.Entities.Giving;

/// Table `distribution` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class Distribution : Entity
{
    public Distribution(Guid sponsoredUnitId, Guid bookFormatId) : base(Guid.CreateVersion7())
    {
        SponsoredUnitId = Guard.NotEmpty(sponsoredUnitId);
        BookFormatId = Guard.NotEmpty(bookFormatId);
    }

    private Distribution()
    {
    }

    public Guid SponsoredUnitId { get; private set; }

    public Guid BookFormatId { get; private set; }

    public DateOnly? DeliveredOn { get; private set; }

    public Guid? ProofAssetId { get; private set; }
}
