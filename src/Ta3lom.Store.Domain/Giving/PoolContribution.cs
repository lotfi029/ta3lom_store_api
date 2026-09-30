using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Domain.Giving;

/// Table `pool_contribution` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class PoolContribution : Entity
{
    public PoolContribution(Guid sponsorshipOfferId, Guid orderLineId, Money amount, Money remaining, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        SponsorshipOfferId = Guard.NotEmpty(sponsorshipOfferId);
        OrderLineId = Guard.NotEmpty(orderLineId);
        Amount = amount;
        Remaining = remaining;
        IsZakat = false;
        CreatedAt = now;
    }

    private PoolContribution()
    {
    }

    public Guid SponsorshipOfferId { get; private set; }

    public Guid OrderLineId { get; private set; }

    public Money Amount { get; private set; }

    public Money Remaining { get; private set; }

    public bool IsZakat { get; private set; }

    public DateTimeOffset? SweptToFundAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
