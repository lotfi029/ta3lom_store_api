namespace Ta3lom.Store.Domain.Entities.Giving;

/// Table `distribution` (RUL-47, OP-9): a sponsored book copy for a beneficiary, in the product's print format; the stock
/// movement `sponsor_distribution` is written when `delivered_on` is set.
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

    public static Distribution ForUnit(Guid unitId, Guid bookFormatId) => new(unitId, bookFormatId);

    /// Manual completion with the delivery date and optional proof (#114).
    public void Deliver(DateOnly deliveredOn, Guid? proofAssetId)
    {
        DeliveredOn = deliveredOn;
        ProofAssetId = proofAssetId;
    }
}
