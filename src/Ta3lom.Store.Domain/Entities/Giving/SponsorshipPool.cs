namespace Ta3lom.Store.Domain.Entities.Giving;

/// Table `sponsorship_pool` (RUL-45, database/01 §4.5, 09 §5.5): one per offer; `balance = Σ remaining` of its open
/// contributions.
public sealed class SponsorshipPool
{
    public SponsorshipPool(Guid sponsorshipOfferId, DateTimeOffset now)
    {
        SponsorshipOfferId = Guard.NotEmpty(sponsorshipOfferId);
        Balance = Money.Zero;
        UpdatedAt = now;
    }

    private SponsorshipPool()
    {
    }

    public Guid SponsorshipOfferId { get; private set; }

    public Money Balance { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// RUL-45. `openFifo` = every open contribution of this offer (locked), including `added`. Converts whole seats while the
    /// balance allows, consuming contributions oldest first; the remainder stays in the pool.
    public PoolingResult Add(PoolContribution added, IReadOnlyList<PoolContribution> openFifo, Guid productId, Money seatPrice, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(added);
        ArgumentNullException.ThrowIfNull(openFifo);
        if (added.SponsorshipOfferId != SponsorshipOfferId || openFifo.Any(c => c.SponsorshipOfferId != SponsorshipOfferId))
        {
            throw new DomainException("pool.offer_mismatch", "The contribution belongs to another offer.");
        }

        if (seatPrice <= Money.Zero)
        {
            throw new DomainException("pool.seat_price", "The seat price must be positive.");
        }

        Balance += added.Amount;
        var queue = new Queue<PoolContribution>(openFifo.Where(c => c.Remaining > Money.Zero).DistinctBy(c => c.Id)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id));
        var units = new List<SponsoredUnit>();
        var fundings = new List<UnitFunding>();

        while (Balance >= seatPrice)
        {
            var unit = SponsoredUnit.Pooled(productId, SponsorshipOfferId, seatPrice, now);
            var need = seatPrice;
            while (need > Money.Zero)
            {
                if (queue.Count == 0)
                {
                    throw new DomainException("pool.invariant", "balance ≠ Σ remaining");
                }

                var c = queue.Peek();                                                        // FIFO
                var take = c.Consume(need);
                fundings.Add(new UnitFunding(unit.Id, c.Id, take));
                if (c.IsZakat)
                {
                    unit.MarkZakatFunded();                                                  // RUL-47d
                }

                need -= take;
                if (c.Remaining == Money.Zero)
                {
                    queue.Dequeue();
                }
            }

            Balance -= seatPrice;
            units.Add(unit);
        }

        if (Balance != queue.Select(c => c.Remaining).Sum())                                // database/01 §4.5 invariant
        {
            throw new DomainException("pool.invariant", "balance ≠ Σ remaining");
        }

        UpdatedAt = now;
        return new PoolingResult(units, fundings);
    }

    /// RUL-46 at campaign end: the non-zakat remainder moves to the general fund (`open` = the locked open contributions);
    /// zakat contributions are never swept and stay in the pool for finance. Returns the swept amount (zero = nothing).
    public Money Sweep(IReadOnlyList<PoolContribution> open, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(open);
        var sweepable = open.Where(c => !c.IsZakat && c.Remaining > Money.Zero && c.SweptToFundAt is null).ToList();
        var amount = sweepable.Select(c => c.Remaining).Sum();
        if (amount == Money.Zero)
        {
            return Money.Zero;
        }

        foreach (var c in sweepable)
        {
            c.Sweep(now);
        }

        Balance -= amount;
        UpdatedAt = now;
        return amount;
    }
}

/// What one pooling step produced: new pooled units and the contributions that funded them.
public sealed record PoolingResult(IReadOnlyList<SponsoredUnit> Units, IReadOnlyList<UnitFunding> Fundings);
