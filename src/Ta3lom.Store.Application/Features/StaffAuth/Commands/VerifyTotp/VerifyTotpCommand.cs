using FluentValidation;

namespace Ta3lom.Store.Application.Features.StaffAuth.Commands.VerifyTotp;

/// Step 2 of staff sign-in (D-12): the TOTP code (±1 step) → access token (10 min, `pwd_change` while a forced change is
/// pending) + refresh family (8 h). The MFA token is single use (its `jti`), and a time step is never accepted twice.
/// Failures count towards the lockout and are audited.
public sealed record VerifyTotpCommand(Guid StaffId, string Code, string MfaTokenId, DateTimeOffset MfaExpiresAt, string? UserAgent) : ICommand<StaffSession>;

internal sealed class VerifyTotpCommandHandler(
    IStaffUserRepository staff,
    ITotpService totp,
    IUsedTokenStore usedTokens,
    StaffSessions sessions,
    IAuditTrail audit,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<VerifyTotpCommand, StaffSession>
{
    public async Task<Result<StaffSession>> HandleAsync(VerifyTotpCommand command, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var user = await staff.GetByIdAsync(command.StaffId, ct);
        if (user is null || !user.IsActive)
        {
            return StaffErrors.TotpRequired;
        }

        if (user.IsLocked(now))
        {
            return StaffErrors.AccountLocked(user.LockedUntil!.Value);
        }

        if (user.TotpSecretEnc is null)
        {
            return StaffErrors.TotpNotEnrolled;
        }

        var step = totp.VerifyStep(user.TotpSecretEnc, command.Code);
        if (step is null || !user.AcceptTotpStep(step.Value))
        {
            var locked = user.RecordFailure(now);
            audit.Record(AuditActions.StaffTotpFailed, "staff_user", user.Id.ToString(), user.Email, new { reason = step is null ? "wrong_code" : "replayed_step" });
            if (locked)
            {
                audit.Record(AuditActions.StaffLocked, "staff_user", user.Id.ToString(), user.Email, new { until = user.LockedUntil });
            }

            await uow.SaveChangesAsync(ct);
            return locked ? StaffErrors.AccountLocked(user.LockedUntil!.Value) : StaffErrors.TotpInvalid;
        }

        if (!usedTokens.TryUse(command.MfaTokenId, command.MfaExpiresAt))
        {
            return StaffErrors.TotpRequired;                                      // the MFA token was already spent
        }

        user.RecordLogin(now);
        var session = sessions.Start(user, command.UserAgent, now);
        audit.Record(AuditActions.StaffLoginSucceeded, "staff_user", user.Id.ToString(), user.Email);
        await uow.SaveChangesAsync(ct);
        return session;
    }
}

internal sealed class VerifyTotpCommandValidator : AbstractValidator<VerifyTotpCommand>
{
    public VerifyTotpCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().Matches("^[0-9]{6}$").WithMessage("code must be 6 digits.");
        RuleFor(c => c.MfaTokenId).NotEmpty();
    }
}
