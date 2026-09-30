using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Features.Outbox.Handlers;
using Ta3lom.Store.Domain.Entities.Fulfilment;

namespace Ta3lom.Store.Application.Features.Enrolments.Commands.LmsProgressSync;

/// `LmsProgressSync` job (hourly, 09 §6; RUL-42, RUL-50): pulls progress and completion from `ILmsGateway` for every
/// `active` enrolment (and `completed` ones still waiting for a certificate number) → `progress_pct`, `started_at`,
/// `completed_at`, `certificate_no`. The LMS is called outside any transaction; each change is saved in its own short
/// transaction, where a completed **sponsored** enrolment also completes its unit (hook, S-25). Idempotent: a second run with
/// the same LMS data changes nothing and completes a unit at most once. Returns the number of enrolments changed.
public sealed record LmsProgressSyncCommand(int BatchSize = 200) : ICommand<int>;

internal sealed partial class LmsProgressSyncCommandHandler(
    IApplicationDbContext db,
    IGenericRepository<Enrolment> enrolments,
    ILmsGateway lms,
    ISponsoredUnitCompletion units,
    IUnitOfWork uow,
    ILogger<LmsProgressSyncCommandHandler> logger) : ICommandHandler<LmsProgressSyncCommand, int>
{
    public async Task<Result<int>> HandleAsync(LmsProgressSyncCommand command, CancellationToken ct = default)
    {
        var changed = 0;
        Guid? after = null;
        while (true)
        {
            var batch = await db.Enrolments.AsNoTracking()
                .Where(e => e.LmsEnrolmentRef != null
                            && (e.Status == EnrolmentStatus.Active || (e.Status == EnrolmentStatus.Completed && e.CertificateNo == null))
                            && (after == null || e.Id.CompareTo(after.Value) > 0))
                .OrderBy(e => e.Id).Take(command.BatchSize).ToListAsync(ct);
            if (batch.Count == 0)
            {
                return changed;
            }

            after = batch[^1].Id;
            foreach (var snapshot in batch)
            {
                LmsProgress? progress;
                try
                {
                    progress = await lms.ProgressAsync(await LmsEnrolHandler.LmsViewAsync(db, snapshot, ct), ct);
                }
                catch (HttpRequestException ex)
                {
                    LogProgressUnavailable(logger, ex, snapshot.Id);
                    continue;                                                              // next run retries
                }

                if (progress is null || !Differs(snapshot, progress))
                {
                    continue;
                }

                await uow.ExecuteInTransactionAsync(async token =>
                {
                    var enrolment = await enrolments.GetByIdAsync(snapshot.Id, token);
                    if (enrolment is null)
                    {
                        return;
                    }

                    var completedNow = enrolment.ApplyProgress(progress.ProgressPct, progress.StartedAt, progress.CompletedAt, progress.CertificateNo);
                    if (completedNow && enrolment.Source == EnrolmentSource.Sponsored && enrolment.SponsoredUnitId is { } unitId)
                    {
                        await units.CompleteAsync(unitId, enrolment.CompletedAt!.Value, token);   // RUL-50, once
                    }
                }, ct);
                changed++;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "LMS progress unavailable for enrolment {EnrolmentId}; retried next hour")]
    private static partial void LogProgressUnavailable(ILogger logger, Exception exception, Guid enrolmentId);

    private static bool Differs(Enrolment e, LmsProgress p) =>
        p.ProgressPct > e.ProgressPct
        || (p.StartedAt is not null && e.StartedAt is null)
        || (p.CompletedAt is not null && e.Status != EnrolmentStatus.Completed)
        || (!string.IsNullOrWhiteSpace(p.CertificateNo) && e.CertificateNo is null);
}

/// S-40 placeholder for the sponsored-unit hook; S-25 replaces it with the real transition.
internal sealed class NoSponsoredUnitCompletion : ISponsoredUnitCompletion
{
    public Task CompleteAsync(Guid sponsoredUnitId, DateTimeOffset completedAt, CancellationToken ct = default) => Task.CompletedTask;
}
