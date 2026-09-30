using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Domain.Catalog;

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
}
