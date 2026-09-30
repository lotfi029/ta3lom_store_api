namespace Ta3lom.Store.Domain.Entities.Giving;

/// Table `pool_contribution` (RUL-45): one per `sponsor` line in `amount` mode; `remaining` is what has not yet been
/// converted into pooled units.
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

    /// RUL-45: the paid line's amount enters the pool in full; zakat intent is carried to the units it funds (RUL-47d).
    public static PoolContribution FromLine(Guid sponsorshipOfferId, Guid orderLineId, Money amount, bool isZakat, DateTimeOffset now) =>
        new(sponsorshipOfferId, orderLineId, amount, amount, now) { IsZakat = isZakat };

    /// FIFO conversion (database/01 §4.5): takes `min(remaining, need)` and returns it.
    public Money Consume(Money need)
    {
        var take = Remaining < need ? Remaining : need;
        Remaining -= take;
        return take;
    }
}
