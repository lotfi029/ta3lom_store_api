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

    /// RUL-09/RUL-10: after ends_at (Riyadh wall time stored as UTC) no new gifts; the target closes the cause
    /// only with `close_on_target`. The general fund is open-ended (ends_at NULL).
    public bool IsOpen(DateTimeOffset now, decimal raised) =>
        now >= StartsAt && (EndsAt is null || now < EndsAt) && !(CloseOnTarget && raised >= TargetAmount);

    /// RUL-07: a unit amount needs a unit label; without units, custom amounts must be allowed.
    public void Update(Money? unitAmount, string? unitLabelAr, string? unitLabelEn, decimal targetAmount, bool allowCustomAmount, Money minAmount,
        bool zakatEligible, string? beneficiariesAr, string? beneficiariesEn, bool closeOnTarget, bool isGeneralFund,
        DateTimeOffset startsAt, DateTimeOffset? endsAt)
    {
        Guard.Against(unitAmount is not null && string.IsNullOrWhiteSpace(unitLabelAr), "cause.unit_label_required");
        Guard.Against(unitAmount is null && !allowCustomAmount, "cause.mode_required");
        Guard.Against(targetAmount <= 0, "cause.target_invalid");
        Guard.Against(endsAt is { } e && e <= startsAt, "cause.dates_invalid");
        UnitAmount = unitAmount;
        UnitLabelAr = unitAmount is null ? null : unitLabelAr;
        UnitLabelEn = unitAmount is null ? null : unitLabelEn;
        TargetAmount = targetAmount;
        AllowCustomAmount = allowCustomAmount;
        MinAmount = minAmount;
        ZakatEligible = zakatEligible;
        BeneficiariesAr = beneficiariesAr;
        BeneficiariesEn = beneficiariesEn;
        CloseOnTarget = closeOnTarget;
        IsGeneralFund = isGeneralFund;
        StartsAt = startsAt;
        EndsAt = endsAt;
    }
}
