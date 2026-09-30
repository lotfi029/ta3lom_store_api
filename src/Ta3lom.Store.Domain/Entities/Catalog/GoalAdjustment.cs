namespace Ta3lom.Store.Domain.Entities.Catalog;

/// Table `goal_adjustment` (RUL-46, FR-S58): signed changes to a product's goal counters outside payments — the pool sweep
/// pair and finance corrections. Integrity check 6 adds them to the paid gift lines.
public sealed class GoalAdjustment
{
    public GoalAdjustment(Guid productId, GoalAdjustmentKind kind, Money amount, string actor, DateTimeOffset now)
    {
        ProductId = Guard.NotEmpty(productId);
        Kind = kind;
        Amount = amount;
        Units = 0;
        At = now;
        Actor = Guard.NotNull(actor);
    }

    private GoalAdjustment()
    {
    }

    public long Id { get; private set; }

    public Guid ProductId { get; private set; }

    public GoalAdjustmentKind Kind { get; private set; }

    public Money Amount { get; private set; }

    public int Units { get; private set; }

    public Guid? SponsorshipOfferId { get; private set; }

    public Guid? PairId { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset At { get; private set; }

    public string Actor { get; private set; } = null!;

    /// RUL-46: the remainder leaves the sponsorship product …
    public static GoalAdjustment PoolSweepOut(Guid productId, Money amount, Guid offerId, Guid pairId, string actor, DateTimeOffset now) =>
        new(productId, GoalAdjustmentKind.PoolSweepOut, new Money(-amount.Amount), actor, now) { SponsorshipOfferId = offerId, PairId = pairId };

    /// … and enters the general education fund (same `pair_id`).
    public static GoalAdjustment PoolSweepIn(Guid generalFundId, Money amount, Guid offerId, Guid pairId, string actor, DateTimeOffset now) =>
        new(generalFundId, GoalAdjustmentKind.PoolSweepIn, amount, actor, now) { SponsorshipOfferId = offerId, PairId = pairId };

    /// FR-S58: finance correction (signed amount and/or units, with a note).
    public static GoalAdjustment Correction(Guid productId, Money amount, int units, string note, string actor, DateTimeOffset now) =>
        new(productId, GoalAdjustmentKind.Correction, amount, actor, now) { Units = units, Note = Guard.NotNull(note) };
}
