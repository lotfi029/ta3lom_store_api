using FluentValidation;

namespace Ta3lom.Store.Application.Features.StaffAuth.Commands.ChangePassword;

/// `POST /v1/admin/auth/password` (BR-A11, D-12): the signed-in staff member changes their own password (current + new). It
/// ends a forced change. Every refresh family of the member is revoked and a fresh session is issued (R-5), so other
/// devices sign in again and the new access token no longer carries `pwd_change`.
public sealed record ChangePasswordCommand(Guid StaffId, string CurrentPassword, string NewPassword, string? UserAgent) : ICommand<StaffSession>;

internal sealed class ChangePasswordCommandHandler(
    IStaffUserRepository staff,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher hasher,
    StaffSessions sessions,
    StaffFailures failures,
    IAuditTrail audit,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<ChangePasswordCommand, StaffSession>
{
    /// A wrong current password counts towards the lockout and is audited (L-7), so the failure is committed.
    public Task<Result<StaffSession>> HandleAsync(ChangePasswordCommand command, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(token => ChangeAsync(command, token), commitOnFailure: true, ct);

    private async Task<Result<StaffSession>> ChangeAsync(ChangePasswordCommand command, CancellationToken ct)
    {
        var user = await staff.GetByIdAsync(command.StaffId, ct);
        if (user is null || !user.IsActive)
        {
            return GeneralErrors.Unauthenticated;
        }

        // A-3 (D-12): while locked the current password is not judged (no guessing through a lock with a stolen token).
        await staff.LockForUpdateAsync(user, ct);
        if (user.IsLocked(clock.UtcNow))
        {
            audit.Record(AuditActions.StaffPasswordChangeFailed, "staff_user", user.Id.ToString(), user.Email, new { reason = "locked" });
            return StaffErrors.AccountLocked(user.LockedUntil!.Value);
        }

        if (!hasher.Verify(user.PassHash, command.CurrentPassword))
        {
            await failures.RecordAsync(user, AuditActions.StaffPasswordChangeFailed, "current_password", ct);
            return StaffErrors.CurrentPasswordInvalid;
        }

        List<string> reasons = [.. PasswordPolicy.Check(command.NewPassword)];
        if (string.Equals(command.NewPassword, command.CurrentPassword, StringComparison.Ordinal))
        {
            reasons.Add("same_as_current");
        }

        if (reasons.Count > 0)
        {
            return StaffErrors.PasswordWeak(reasons);
        }

        var now = clock.UtcNow;
        user.ChangePassword(hasher.Hash(command.NewPassword), now);
        await refreshTokens.RevokeAllForSubjectAsync(Domain.Enums.TokenSubjectType.Staff, user.Id, now, ct);
        var session = sessions.Start(user, command.UserAgent, now);
        audit.Record(AuditActions.StaffPasswordChanged, "staff_user", user.Id.ToString(), user.Email);
        return session;
    }
}

internal sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(c => c.CurrentPassword).NotEmpty().MaximumLength(256);
        RuleFor(c => c.NewPassword).NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
    }
}
