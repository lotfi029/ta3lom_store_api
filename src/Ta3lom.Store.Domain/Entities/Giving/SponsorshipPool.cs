namespace Ta3lom.Store.Domain.Entities.Giving;

/// Table `sponsorship_pool` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
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
}
