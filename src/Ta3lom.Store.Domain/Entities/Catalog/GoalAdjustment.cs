namespace Ta3lom.Store.Domain.Entities.Catalog;

/// Table `goal_adjustment` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
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
}
