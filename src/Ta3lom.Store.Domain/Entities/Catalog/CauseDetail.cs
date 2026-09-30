namespace Ta3lom.Store.Domain.Entities.Catalog;

/// Table `cause_detail` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class CauseDetail
{
    public CauseDetail(Guid productId, decimal targetAmount, DateTimeOffset now)
    {
        ProductId = Guard.NotEmpty(productId);
        TargetAmount = targetAmount;
        AllowCustomAmount = true;
        MinAmount = new Money(10m);
        ZakatEligible = false;
        CloseOnTarget = false;
        IsGeneralFund = false;
        StartsAt = now;
    }

    private CauseDetail()
    {
    }

    public Guid ProductId { get; private set; }

    public Money? UnitAmount { get; private set; }

    public string? UnitLabelAr { get; private set; }

    public string? UnitLabelEn { get; private set; }

    public decimal TargetAmount { get; private set; }

    public bool AllowCustomAmount { get; private set; }

    public Money MinAmount { get; private set; }

    public bool ZakatEligible { get; private set; }

    public string? BeneficiariesAr { get; private set; }

    public string? BeneficiariesEn { get; private set; }

    public bool CloseOnTarget { get; private set; }

    public bool IsGeneralFund { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset? EndsAt { get; private set; }
}
