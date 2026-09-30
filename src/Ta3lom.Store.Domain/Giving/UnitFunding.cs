using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Domain.Giving;

/// Table `unit_funding` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class UnitFunding
{
    public UnitFunding(Guid sponsoredUnitId, Guid poolContributionId, Money amount)
    {
        SponsoredUnitId = Guard.NotEmpty(sponsoredUnitId);
        PoolContributionId = Guard.NotEmpty(poolContributionId);
        Amount = amount;
    }

    private UnitFunding()
    {
    }

    public Guid SponsoredUnitId { get; private set; }

    public Guid PoolContributionId { get; private set; }

    public Money Amount { get; private set; }
}
