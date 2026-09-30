namespace Ta3lom.Store.Application.Interfaces;

/// Hook called by `LmsProgressSync` inside the enrolment's transaction when a **sponsored** enrolment completes (RUL-50:
/// `allocated → completed`, `sponsored_unit_event`, outbox `SponsorUnitCompleted`). S-40 registers a no-op; S-25 fills it
/// (owner fix 1).
public interface ISponsoredUnitCompletion
{
    Task CompleteAsync(Guid sponsoredUnitId, DateTimeOffset completedAt, CancellationToken ct = default);
}
