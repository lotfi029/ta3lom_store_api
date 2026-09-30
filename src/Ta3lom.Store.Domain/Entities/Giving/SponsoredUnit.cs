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

    /// RUL-47/50: `awaiting_allocation → allocated` (policy checked by the caller); raises `SponsorUnitAllocated`.
    public SponsoredUnitEvent Allocate(Guid beneficiaryId, Guid batchId, string actor, DateTimeOffset now)
    {
        if (Status != UnitStatus.AwaitingAllocation)
        {
            throw new DomainException("unit.not_awaiting", "Only awaiting units can be allocated.");
        }

        BeneficiaryId = Guard.NotEmpty(beneficiaryId);
        AllocationBatchId = Guard.NotEmpty(batchId);
        AllocatedAt = now;
        AllocatedBy = Guard.NotNull(actor);
        Raise(new Events.SponsorUnitAllocated(Id, ProductId, OrderLineId));
        return Move(UnitStatus.Allocated, actor, now);
    }

    /// RUL-50: `allocated → completed` (course finished or copy delivered); raises `SponsorUnitCompleted`. Null when the unit
    /// is not allocated (idempotent for the sync job; the manual route maps it to `unit.invalid_transition`).
    public SponsoredUnitEvent? Complete(string actor, DateTimeOffset now)
    {
        if (Status != UnitStatus.Allocated)
        {
            return null;
        }

        CompletedAt = now;
        Raise(new Events.SponsorUnitCompleted(Id, ProductId, OrderLineId));
        return Move(UnitStatus.Completed, actor, now);
    }

    /// RUL-50: `allocated → lapsed → awaiting_allocation`; keeps `funded_at`, so it returns to the front of the FIFO queue
    /// (RUL-48). Returns both events; the former beneficiary is returned for the SMS.
    public (IReadOnlyList<SponsoredUnitEvent> Events, Guid FormerBeneficiaryId) Lapse(string actor, DateTimeOffset now)
    {
        if (Status != UnitStatus.Allocated || BeneficiaryId is not { } former)
        {
            throw new DomainException("unit.not_allocated", "Only allocated units lapse.");
        }

        LapsedAt = now;
        var lapsed = Move(UnitStatus.Lapsed, actor, now);
        BeneficiaryId = null;
        AllocationBatchId = null;
        AllocatedAt = null;
        AllocatedBy = null;
        return ([lapsed, Move(UnitStatus.AwaitingAllocation, actor, now)], former);
    }

    private SponsoredUnitEvent Move(UnitStatus to, string actor, DateTimeOffset now)
    {
        var from = Status;
        Status = to;
        return SponsoredUnitEvent.Transition(Id, from, to, actor, now);
    }
}
