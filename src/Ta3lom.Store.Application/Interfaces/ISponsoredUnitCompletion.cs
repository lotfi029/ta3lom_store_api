namespace Ta3lom.Store.Application.Interfaces;

/// Hook called by `LmsProgressSync` inside the enrolment's transaction when a **sponsored** enrolment completes (RUL-50:
/// `allocated → completed`, `sponsored_unit_event`, outbox `SponsorUnitCompleted`). Implemented in S-25
/// (`SponsoredUnitCompletion`, owner fix 1).
public interface ISponsoredUnitCompletion
{
    Task CompleteAsync(Guid sponsoredUnitId, DateTimeOffset completedAt, CancellationToken ct = default);
}
