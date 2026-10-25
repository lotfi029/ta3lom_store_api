using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Domain.Events;

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
    StaffFailures failures,
    IOutbox outbox,
    IApplicationDbContext db,
    IAuditTrail audit,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<VerifyTotpCommand, StaffSession>
{
    public const string KeyLostKind = "totp_key_lost";

    /// The whole step runs on the locked row (A-1): parallel codes serialise, so failures add up and a time step is used once.
    public Task<Result<StaffSession>> HandleAsync(VerifyTotpCommand command, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(token => VerifyAsync(command, token), commitOnFailure: true, ct);

    private async Task<Result<StaffSession>> VerifyAsync(VerifyTotpCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var user = await staff.GetByIdAsync(command.StaffId, ct);
        if (user is null || !user.IsActive)
        {
            return StaffErrors.TotpRequired;
        }

        await staff.LockForUpdateAsync(user, ct);

        if (user.IsLocked(now))
        {
            return StaffErrors.AccountLocked(user.LockedUntil!.Value);
        }

        if (user.TotpSecretEnc is null)
        {
            return StaffErrors.TotpNotEnrolled;
        }

        var check = totp.VerifyStep(user.TotpSecretEnc, command.Code);
        if (check.KeyUnavailable)
        {
            // L-9: not the member's fault: no failure is counted, the enrolment stays, staff are alerted; an admin resets it.
            // R11-1: one alert per member per 24 h (the locked row serialises the attempts).
            if (!await KeyLossAlertedAsync(user.Id, now, ct))
            {
                outbox.Enqueue(new StaffAlert(KeyLostKind, user.Id.ToString(), "The TOTP secret of a staff member cannot be decrypted (Data Protection key ring lost). Reset the authenticator."));
            }

            return StaffErrors.TotpUnavailable;
        }

        var step = check.Step;
        if (step is null || !user.AcceptTotpStep(step.Value))
        {
            var outcome = await failures.RecordAsync(user, AuditActions.StaffTotpFailed, step is null ? "wrong_code" : "replayed_step", ct);
            return outcome == Domain.Entities.Identity.FailureOutcome.LockedNow ? StaffErrors.AccountLocked(user.LockedUntil!.Value) : StaffErrors.TotpInvalid;
        }

        if (!usedTokens.TryUse(command.MfaTokenId, command.MfaExpiresAt))
        {
            return StaffErrors.TotpRequired;                                      // the MFA token was already spent
        }

        user.RecordLogin(now);
        var session = sessions.Start(user, command.UserAgent, now);
        audit.Record(AuditActions.StaffLoginSucceeded, "staff_user", user.Id.ToString(), user.Email);
        return session;
    }

    private async Task<bool> KeyLossAlertedAsync(Guid staffId, DateTimeOffset now, CancellationToken ct)
    {
        var since = now - TimeSpan.FromHours(24);
        var reference = staffId.ToString();
        var payloads = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == nameof(StaffAlert) && m.OccurredAt >= since).Select(m => m.Payload).ToListAsync(ct);   // jsonb: filtered here
        return payloads.Exists(p => p.Contains(KeyLostKind, StringComparison.Ordinal) && p.Contains(reference, StringComparison.Ordinal));
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
