using Ta3lom.Store.Application.DTOs.Account;
using Ta3lom.Store.Application.Features.Outbox.Handlers;
using Ta3lom.Store.Domain.Entities.Fulfilment;

namespace Ta3lom.Store.Application.Features.Enrolments.Commands.LaunchCourse;

/// `POST /v1/me/enrolments/{id}/launch` (#29, FR-S23, RUL-42): the owner's active enrolment only; the first launch records
/// `started_at` (RUL-50 lapse). `enrolment.not_active` for pending, revoked or expired access.
public sealed record LaunchCourseCommand(Guid EnrolmentId, string? Locale) : ICommand<LaunchResponse>;

internal sealed class LaunchCourseCommandHandler(
    ICurrentActor actor,
    IGenericRepository<Enrolment> enrolments,
    Abstractions.Data.IApplicationDbContext db,
    ILmsGateway lms,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<LaunchCourseCommand, LaunchResponse>
{
    public async Task<Result<LaunchResponse>> HandleAsync(LaunchCourseCommand command, CancellationToken ct = default)
    {
        var enrolment = await enrolments.GetByIdAsync(command.EnrolmentId, ct);
        if (enrolment is null || actor.Kind != ActorKind.Customer || enrolment.CustomerId != actor.Id)
        {
            return GeneralErrors.NotFound;
        }

        var now = clock.UtcNow;
        if (!enrolment.CanLaunch(now))
        {
            return EnrolmentErrors.NotActive;
        }

        var launch = await lms.LaunchAsync(await LmsEnrolHandler.LmsViewAsync(db, enrolment, ct), command.Locale is "en" ? "en" : "ar", ct);
        enrolment.RecordLaunch(now);
        await uow.SaveChangesAsync(ct);
        return new LaunchResponse(launch.Url, launch.ExpiresAt);
    }
}
