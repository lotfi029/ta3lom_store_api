using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.AdminOps.Commands;

/// `IntegrityChecks` job (04:00 daily, database/03 §6): every failing check of `db/reference/003` raises one staff alert
/// (`FinanceAlert` kind `integrity_check_failed`). Returns the number of failing checks.
public sealed record IntegrityChecksCommand : ICommand<int>;

internal sealed class IntegrityChecksCommandHandler(IOperationsStatus ops, IOutbox outbox, IUnitOfWork uow) : ICommandHandler<IntegrityChecksCommand, int>
{
    public const string AlertKind = "integrity_check_failed";

    public async Task<Result<int>> HandleAsync(IntegrityChecksCommand command, CancellationToken ct = default)
    {
        var failing = (await ops.RunIntegrityChecksAsync(ct)).Where(c => c.Rows > 0).ToList();
        foreach (var c in failing)
        {
            outbox.Enqueue(new FinanceAlert(AlertKind, null, null, $"check {c.Number} — {c.Title}: {c.Rows} row(s)"));
        }

        if (failing.Count > 0)
        {
            await uow.SaveChangesAsync(ct);
        }

        return failing.Count;
    }
}

/// `Retention` job (Fridays 04:30, database/03 §7, PDPL): `payment_event.payload` scrubbed after 18 months; notifications and
/// download logs deleted after 12 months; processed outbox rows after 30 days; beneficiary PII erased two years after the
/// beneficiary became inactive or was rejected (code and history kept). Returns the rows touched.
public sealed record RetentionCommand : ICommand<int>;

internal sealed class RetentionCommandHandler(IOperationsMaintenance maintenance, IApplicationDbContext db, IGenericRepository<Beneficiary> beneficiaries,
    IUnitOfWork uow, IClock clock) : ICommandHandler<RetentionCommand, int>
{
    public async Task<Result<int>> HandleAsync(RetentionCommand command, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var touched = await maintenance.ScrubPaymentPayloadsAsync(now.AddMonths(-18), ct)
                      + await maintenance.DeleteNotificationsAsync(now.AddMonths(-12), ct)
                      + await maintenance.DeleteProcessedOutboxAsync(now.AddDays(-30), ct)
                      + await maintenance.DeleteDownloadLogAsync(now.AddMonths(-12), ct);

        var cutoff = now.AddYears(-2);
        var ids = await db.Beneficiaries.AsNoTracking()
            .Where(b => (b.EligibilityStatus == EligibilityStatus.Inactive || b.EligibilityStatus == EligibilityStatus.Rejected)
                        && b.VerifiedAt != null && b.VerifiedAt < cutoff)
            .Select(b => b.Id).ToListAsync(ct);                                              // names are encrypted: checked in memory
        foreach (var id in ids)
        {
            var b = await beneficiaries.GetByIdAsync(id, ct);
            if (b is { PiiErased: false })
            {
                b.ErasePii();
                touched++;
            }
        }

        await uow.SaveChangesAsync(ct);
        return touched;
    }
}

/// `PruneIdempotencyKeys` job (hourly, 11 §1.5): keys older than 24 h. Returns the rows deleted.
public sealed record PruneIdempotencyKeysCommand : ICommand<int>;

internal sealed class PruneIdempotencyKeysCommandHandler(IOperationsMaintenance maintenance, IClock clock) : ICommandHandler<PruneIdempotencyKeysCommand, int>
{
    public async Task<Result<int>> HandleAsync(PruneIdempotencyKeysCommand command, CancellationToken ct = default) =>
        await maintenance.PruneIdempotencyKeysAsync(clock.UtcNow.AddHours(-24), ct);
}
