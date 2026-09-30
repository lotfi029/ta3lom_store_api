using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Domain.Giving;

/// Table `sponsored_unit` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class SponsoredUnit : AggregateRoot
{
    public SponsoredUnit(Guid productId, Guid sponsorshipOfferId, UnitSource source, Money unitPrice, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        ProductId = Guard.NotEmpty(productId);
        SponsorshipOfferId = Guard.NotEmpty(sponsorshipOfferId);
        Source = source;
        UnitPrice = unitPrice;
        HasZakat = false;
        Status = UnitStatus.AwaitingAllocation;
        FundedAt = now;
    }

    private SponsoredUnit()
    {
    }

    public Guid ProductId { get; private set; }

    public Guid SponsorshipOfferId { get; private set; }

    public UnitSource Source { get; private set; }

    public Guid? OrderLineId { get; private set; }

    public Money UnitPrice { get; private set; }

    public bool HasZakat { get; private set; }

    public UnitStatus Status { get; private set; }

    public Guid? BeneficiaryId { get; private set; }

    public Guid? AllocationBatchId { get; private set; }

    public DateTimeOffset FundedAt { get; private set; }

    public DateTimeOffset? AllocatedAt { get; private set; }

    public string? AllocatedBy { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? LapsedAt { get; private set; }
}
