namespace Ta3lom.Store.Domain.Entities.Giving;

/// Table `sponsored_unit` (RUL-43 … RUL-50): one fundable seat or copy. Created `awaiting_allocation` by settlement, either
/// `full` (one per unit of a `units`-mode line) or `pooled` (converted from pool contributions).
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

    /// RUL-43: one unit per purchased unit of a `units`-mode sponsor line; zakat intent marks it (RUL-47d).
    public static SponsoredUnit Full(Guid productId, Guid offerId, Guid orderLineId, Money unitPrice, bool isZakat, DateTimeOffset now) =>
        new(productId, offerId, UnitSource.Full, unitPrice, now) { OrderLineId = Guard.NotEmpty(orderLineId), HasZakat = isZakat };

    /// RUL-45: a unit converted from the pool at the offer's seat price.
    public static SponsoredUnit Pooled(Guid productId, Guid offerId, Money seatPrice, DateTimeOffset now) =>
        new(productId, offerId, UnitSource.Pooled, seatPrice, now);

    /// RUL-47d: any zakat contribution in its funding makes the unit zakat-funded.
    public void MarkZakatFunded() => HasZakat = true;
}
