namespace Ta3lom.Store.Domain.Entities.Catalog;

/// Table `goal_progress` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class GoalProgress
{
    public GoalProgress(Guid productId, DateTimeOffset now)
    {
        ProductId = Guard.NotEmpty(productId);
        RaisedAmount = 0m;
        FundedUnits = 0;
        DonorCount = 0;
        ZakatAmount = 0m;
        SeedRaised = 0m;
        SeedUnits = 0;
        SeedDonors = 0;
        UpdatedAt = now;
    }

    private GoalProgress()
    {
    }

    public Guid ProductId { get; private set; }

    public decimal RaisedAmount { get; private set; }

    public int FundedUnits { get; private set; }

    public int DonorCount { get; private set; }

    public decimal ZakatAmount { get; private set; }

    public decimal SeedRaised { get; private set; }

    public int SeedUnits { get; private set; }

    public int SeedDonors { get; private set; }

    public DateTimeOffset? LastGiftAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// Opening balances from offline giving before launch (database/03 §3); audited by the interceptor.
    public void SetSeed(decimal seedRaised, int seedUnits, int seedDonors, DateTimeOffset now)
    {
        Guard.Against(seedRaised < 0 || seedUnits < 0 || seedDonors < 0, "goal.seed_negative");
        SeedRaised = seedRaised;
        SeedUnits = seedUnits;
        SeedDonors = seedDonors;
        UpdatedAt = now;
    }
}
