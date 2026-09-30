namespace Ta3lom.Store.Domain.Entities.Giving;

/// Table `sponsored_unit_event` (RUL-50): every unit transition, time-stamped with its actor.
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

    /// A transition `from → to` (null `from` = the unit was just funded).
    public static SponsoredUnitEvent Transition(Guid unitId, UnitStatus? from, UnitStatus to, string actor, DateTimeOffset now) =>
        new(unitId, to.ToWire(), actor, now) { FromStatus = from?.ToWire() };
}
