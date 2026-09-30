namespace Ta3lom.Store.Domain.Entities.Catalog;

/// Table `sponsorship_offer` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class SponsorshipOffer : AggregateRoot
{
    public SponsorshipOffer(Guid productId, Money seatPrice, int targetUnits, string beneficiaryGroup, string groupLabelAr, DateTimeOffset endsAt, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        ProductId = Guard.NotEmpty(productId);
        SeatPrice = seatPrice;
        UnitLabelAr = "مقعد";
        UnitLabelEn = "seat";
        TargetUnits = targetUnits;
        BeneficiaryGroup = Guard.NotNull(beneficiaryGroup);
        GroupLabelAr = Guard.NotNull(groupLabelAr);
        AllowPartial = true;
        ZakatEligible = false;
        CloseOnTarget = false;
        StartsAt = now;
        EndsAt = endsAt;
        IsActive = true;
        CreatedAt = now;
    }

    private SponsorshipOffer()
    {
    }

    public Guid ProductId { get; private set; }

    public Money SeatPrice { get; private set; }

    public string UnitLabelAr { get; private set; } = null!;

    public string UnitLabelEn { get; private set; } = null!;

    public int TargetUnits { get; private set; }

    public string BeneficiaryGroup { get; private set; } = null!;

    public string GroupLabelAr { get; private set; } = null!;

    public string? GroupLabelEn { get; private set; }

    public bool AllowPartial { get; private set; }

    public bool ZakatEligible { get; private set; }

    public bool CloseOnTarget { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// RUL-09/RUL-10: open between starts_at and ends_at while active; reaching the target closes it only when
    /// `close_on_target` is set.
    public bool IsOpen(DateTimeOffset now, int fundedUnits) =>
        IsActive && now >= StartsAt && now < EndsAt && !(CloseOnTarget && fundedUnits >= TargetUnits);

    public static SponsorshipOffer Create(Guid productId, Money seatPrice, string unitLabelAr, string unitLabelEn, int targetUnits, string beneficiaryGroup,
        string groupLabelAr, string? groupLabelEn, bool allowPartial, bool closeOnTarget, DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset now)
    {
        Guard.Against(endsAt <= startsAt, "offer.dates_invalid");
        var offer = new SponsorshipOffer(productId, seatPrice, targetUnits, beneficiaryGroup, groupLabelAr, endsAt, now);
        offer.Update(seatPrice, unitLabelAr, unitLabelEn, targetUnits, beneficiaryGroup, groupLabelAr, groupLabelEn, allowPartial, closeOnTarget, startsAt, endsAt);
        return offer;
    }

    /// RUL-06: a seat-price change applies to new cart lines only (orders keep their line price).
    public void Update(Money seatPrice, string unitLabelAr, string unitLabelEn, int targetUnits, string beneficiaryGroup, string groupLabelAr,
        string? groupLabelEn, bool allowPartial, bool closeOnTarget, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        Guard.Against(endsAt <= startsAt, "offer.dates_invalid");
        Guard.Against(!seatPrice.IsPositive, "offer.seat_price_invalid");
        Guard.Against(targetUnits <= 0, "offer.target_invalid");
        SeatPrice = seatPrice;
        UnitLabelAr = Guard.NotEmpty(unitLabelAr);
        UnitLabelEn = Guard.NotEmpty(unitLabelEn);
        TargetUnits = targetUnits;
        BeneficiaryGroup = Guard.NotEmpty(beneficiaryGroup);
        GroupLabelAr = Guard.NotEmpty(groupLabelAr);
        GroupLabelEn = groupLabelEn;
        AllowPartial = allowPartial;
        CloseOnTarget = closeOnTarget;
        StartsAt = startsAt;
        EndsAt = endsAt;
    }

    /// Staff close (#71) or campaign end (S-42): no new lines; the pool sweep is separate (RUL-46).
    public void Close() => IsActive = false;
}
