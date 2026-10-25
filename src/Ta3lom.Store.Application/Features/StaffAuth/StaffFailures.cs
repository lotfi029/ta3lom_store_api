using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Application.Features.StaffAuth;

/// Records one failed staff attempt (wrong password, wrong TOTP code, wrong current password) on the locked row, so parallel
/// attempts serialise and none is lost (A-1). Call inside `IUnitOfWork.ExecuteInTransactionAsync`.
internal sealed class StaffFailures(IStaffUserRepository staff, IAuditTrail audit, IClock clock)
{
    public async Task<FailureOutcome> RecordAsync(StaffUser user, string auditAction, string reason, CancellationToken ct)
    {
        await staff.LockForUpdateAsync(user, ct);
        var now = clock.UtcNow;
        var outcome = user.RecordFailure(now);
        audit.Record(auditAction, "staff_user", user.Id.ToString(), user.Email,
            new { reason = outcome == FailureOutcome.WhileLocked ? "locked" : reason });
        if (outcome == FailureOutcome.LockedNow)
        {
            audit.Record(AuditActions.StaffLocked, "staff_user", user.Id.ToString(), user.Email, new { until = user.LockedUntil });
        }

        return outcome;
    }
}
