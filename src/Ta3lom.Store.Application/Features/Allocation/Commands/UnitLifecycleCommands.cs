using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Allocation;
using Ta3lom.Store.Domain.Entities.Fulfilment;
using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Allocation.Commands;

/// `POST /v1/admin/units/{unitId}/complete` (#114; RUL-50, OP-9): a book copy was delivered — `allocated → completed`, the
/// distribution gets its date and proof, and the print stock leaves with a `sponsor_distribution` movement. Course seats
/// complete through `LmsProgressSync` only; anything but an allocated book unit → `unit.invalid_transition`.
public sealed record CompleteUnitCommand(Guid UnitId, DateOnly? DeliveredOn, Guid? ProofAssetId) : ICommand<UnitRow>;

internal sealed class CompleteUnitCommandHandler(IApplicationDbContext db, ISponsoredUnitRepository units, IGenericRepository<Distribution> distributions,
    IGenericRepository<SponsoredUnitEvent> events, IStockRepository stock, IGenericRepository<StockMovement> movements, AllocationReader reader,
    ICurrentActor actor, IUnitOfWork uow, IClock clock) : ICommandHandler<CompleteUnitCommand, UnitRow>
{
    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);

    public async Task<Result<UnitRow>> HandleAsync(CompleteUnitCommand command, CancellationToken ct = default)
    {
        if (command.ProofAssetId is { } proof && !await db.MediaAssets.AnyAsync(m => m.Id == proof, ct))
        {
            return Error.Validation(new Dictionary<string, string[]> { ["proofAssetId"] = ["unknown media asset"] });
        }

        var by = actor.Name ?? "staff";
        var result = await uow.ExecuteInTransactionAsync(async token =>
        {
            var now = clock.UtcNow;
            var unit = await units.LockAsync(command.UnitId, token);
            if (unit is null)
            {
                return Result.Failure<SponsoredUnit>(Error.NotFound(ErrorCodes.NotFound, "Unit not found."));
            }

            var distribution = await distributions.FirstOrDefaultAsync(d => d.SponsoredUnitId == unit.Id, token);
            if (distribution is null || unit.Status != UnitStatus.Allocated)
            {
                return Result.Failure<SponsoredUnit>(AllocationErrors.UnitInvalidTransition);
            }

            var on = command.DeliveredOn ?? DateOnly.FromDateTime(now.ToOffset(Riyadh).DateTime);
            distribution.Deliver(on, command.ProofAssetId);
            if (!await stock.TryAdjustAsync(distribution.BookFormatId, -1, token))                   // OP-9
            {
                return Result.Failure<SponsoredUnit>(FulfilmentErrors.StockBelowReserved);
            }

            movements.Add(StockMovement.Record(distribution.BookFormatId, -1, StockMovementReason.SponsorDistribution, "unit", unit.Id.ToString(),
                null, by, now));
            events.Add(unit.Complete(by, now)!);
            return Result.Success(unit);
        }, ct);
        return result.IsFailure ? result.Error : (await reader.RowsAsync([result.Value], ct))[0];
    }
}

/// RUL-50 (owner fix 1: fills the S-40 hook): a sponsored course completed in the LMS completes its unit once, in the
/// enrolment's transaction (`sponsored_unit_event`, outbox `SponsorUnitCompleted`).
internal sealed class SponsoredUnitCompletion(ISponsoredUnitRepository units, IGenericRepository<SponsoredUnitEvent> events) : ISponsoredUnitCompletion
{
    public async Task CompleteAsync(Guid sponsoredUnitId, DateTimeOffset completedAt, CancellationToken ct = default)
    {
        var unit = await units.LockAsync(sponsoredUnitId, ct);
        if (unit?.Complete("lms", completedAt) is { } transition)
        {
            events.Add(transition);
        }
    }
}

/// `LapseUnits` job (02:00 daily, 09 §6; RUL-50): an allocated course seat whose sponsored enrolment has not started
/// `lapse_days` after allocation → `lapsed → awaiting_allocation` (original `funded_at`, so it heads the FIFO queue), the
/// enrolment `revoked` (`LmsRevoke`), and a short SMS to the former beneficiary (`UnitLapsed`). One transaction per unit.
public sealed record LapseUnitsCommand(int BatchSize = 200) : ICommand<int>;

internal sealed class LapseUnitsCommandHandler(IApplicationDbContext db, IStoreSettings settings, ISponsoredUnitRepository units,
    IGenericRepository<Enrolment> enrolments, IGenericRepository<SponsoredUnitEvent> events, IOutbox outbox, IUnitOfWork uow, IClock clock)
    : ICommandHandler<LapseUnitsCommand, int>
{
    public const string Actor = "job:LapseUnits";

    public async Task<Result<int>> HandleAsync(LapseUnitsCommand command, CancellationToken ct = default)
    {
        var days = (await settings.GetAsync(ct)).LapseDays;
        var cutoff = clock.UtcNow.AddDays(-days);
        var due = await (from u in db.SponsoredUnits.AsNoTracking()
                         join e in db.Enrolments.AsNoTracking() on u.Id equals e.SponsoredUnitId
                         where u.Status == UnitStatus.Allocated && u.AllocatedAt < cutoff && e.StartedAt == null
                               && (e.Status == EnrolmentStatus.PendingSync || e.Status == EnrolmentStatus.Active)
                         orderby u.AllocatedAt
                         select new { UnitId = u.Id, EnrolmentId = e.Id }).Take(command.BatchSize).ToListAsync(ct);
        var lapsed = 0;
        foreach (var d in due)
        {
            var done = await uow.ExecuteInTransactionAsync(async token =>
            {
                var now = clock.UtcNow;
                var unit = await units.LockAsync(d.UnitId, token);
                var enrolment = await enrolments.GetByIdAsync(d.EnrolmentId, token);
                if (unit is not { Status: UnitStatus.Allocated } || enrolment is null || enrolment.StartedAt is not null || !enrolment.IsLive)
                {
                    return Result.Success(false);                                                    // started or changed meanwhile
                }

                enrolment.Revoke();
                outbox.Enqueue(new LmsRevoke(enrolment.Id));
                var (transitions, former) = unit.Lapse(Actor, now);
                events.AddRange(transitions);
                outbox.Enqueue(new UnitLapsed(unit.Id, former, unit.ProductId));
                return Result.Success(true);
            }, ct);
            lapsed += done is { IsSuccess: true, Value: true } ? 1 : 0;
        }

        return lapsed;
    }
}

/// `AllocationAlerts` job (07:00 daily, 09 §6; RUL-49): per product, units awaiting allocation longer than
/// `allocation_alert_days` → `AllocationAlert` (staff); longer than `allocation_escalate_days` → `AllocationEscalation`
/// (manager). At most one of each per product per Riyadh day. Returns the number of messages queued.
public sealed record RaiseAllocationAlertsCommand : ICommand<int>;

internal sealed class RaiseAllocationAlertsCommandHandler(IApplicationDbContext db, IStoreSettings settings, IOutbox outbox, IUnitOfWork uow, IClock clock)
    : ICommandHandler<RaiseAllocationAlertsCommand, int>
{
    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3);

    public async Task<Result<int>> HandleAsync(RaiseAllocationAlertsCommand command, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var now = clock.UtcNow;
        var day = DateOnly.FromDateTime(now.ToOffset(Riyadh).DateTime);
        var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), Riyadh).ToUniversalTime();
        var alertCutoff = now.AddDays(-s.AllocationAlertDays);
        var escalateCutoff = now.AddDays(-s.AllocationEscalateDays);

        var overdue = await (from u in db.SponsoredUnits.AsNoTracking()
                             join p in db.Products.AsNoTracking() on u.ProductId equals p.Id
                             where u.Status == UnitStatus.AwaitingAllocation && u.FundedAt < alertCutoff
                             group u by new { u.ProductId, p.Slug } into g
                             select new
                             {
                                 g.Key.ProductId,
                                 g.Key.Slug,
                                 Units = g.Count(),
                                 Escalated = g.Count(u => u.FundedAt < escalateCutoff),
                                 Oldest = g.Min(u => u.FundedAt),
                             }).ToListAsync(ct);
        if (overdue.Count == 0)
        {
            return 0;
        }

        var types = new[] { nameof(AllocationAlert), nameof(AllocationEscalation) };
        var sentToday = await db.OutboxMessages.AsNoTracking().Where(m => types.Contains(m.Type) && m.OccurredAt >= dayStart)
            .Select(m => new { m.Type, m.Payload }).ToListAsync(ct);
        bool Sent(string type, Guid productId) => sentToday.Exists(m => m.Type == type && m.Payload.Contains(productId.ToString(), StringComparison.OrdinalIgnoreCase));

        var queued = 0;
        foreach (var o in overdue)
        {
            if (!Sent(nameof(AllocationAlert), o.ProductId))
            {
                outbox.Enqueue(new AllocationAlert(o.ProductId, o.Slug, o.Units, o.Oldest, day));
                queued++;
            }

            if (o.Escalated > 0 && !Sent(nameof(AllocationEscalation), o.ProductId))
            {
                outbox.Enqueue(new AllocationEscalation(o.ProductId, o.Slug, o.Escalated, o.Oldest, day));
                queued++;
            }
        }

        if (queued > 0)
        {
            await uow.SaveChangesAsync(ct);
        }

        return queued;
    }
}
