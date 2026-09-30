namespace Ta3lom.Store.Domain.Entities.Giving;

/// Table `sponsored_unit_event` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class SponsoredUnitEvent
{
    public SponsoredUnitEvent(Guid unitId, string toStatus, string actor, DateTimeOffset now)
    {
        UnitId = Guard.NotEmpty(unitId);
        ToStatus = Guard.NotNull(toStatus);
        At = now;
        Actor = Guard.NotNull(actor);
    }

    private SponsoredUnitEvent()
    {
    }

    public long Id { get; private set; }

    public Guid UnitId { get; private set; }

    public string? FromStatus { get; private set; }

    public string ToStatus { get; private set; } = null!;

    public DateTimeOffset At { get; private set; }

    public string Actor { get; private set; } = null!;
}
