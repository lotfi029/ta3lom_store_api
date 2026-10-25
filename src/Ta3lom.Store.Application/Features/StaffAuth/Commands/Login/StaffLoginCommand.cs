using FluentValidation;
using Ta3lom.Store.Application.DTOs.Staff;

namespace Ta3lom.Store.Application.Features.StaffAuth.Commands.Login;

/// Step 1 of staff sign-in (BR-A11, D-12): email + password → 5-minute MFA token. The password is checked before the lock
/// state, so a wrong password answers the same whether the account is locked, unknown or inactive (no enumeration). Five
/// failures within 15 minutes lock the account for one hour. Every outcome is audited (BR-A13).
public sealed record StaffLoginCommand(string Email, string Password) : ICommand<StaffLoginResponse>;

internal sealed class StaffLoginCommandHandler(
    IStaffUserRepository staff,
    IPasswordHasher hasher,
    IAccessTokenIssuer issuer,
    IAuditTrail audit,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<StaffLoginCommand, StaffLoginResponse>
{
    public async Task<Result<StaffLoginResponse>> HandleAsync(StaffLoginCommand command, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var email = command.Email.Trim();
        var user = await staff.FindByEmailAsync(email, ct);
        if (user is null || !user.IsActive)
        {
            hasher.SimulateVerify(command.Password);
            audit.Record(AuditActions.StaffLoginFailed, "staff_user", user?.Id.ToString(), "anonymous", new { reason = user is null ? "unknown" : "inactive" });
            await uow.SaveChangesAsync(ct);
            return StaffErrors.InvalidCredentials;
        }

        if (!hasher.Verify(user.PassHash, command.Password))
        {
            var lockedNow = !user.IsLocked(now) && user.RecordFailure(now);
            audit.Record(AuditActions.StaffLoginFailed, "staff_user", user.Id.ToString(), user.Email, new { reason = "password" });
            if (lockedNow)
            {
                audit.Record(AuditActions.StaffLocked, "staff_user", user.Id.ToString(), user.Email, new { until = user.LockedUntil });
            }

            await uow.SaveChangesAsync(ct);
            return StaffErrors.InvalidCredentials;
        }

        if (user.IsLocked(now))
        {
            audit.Record(AuditActions.StaffLoginFailed, "staff_user", user.Id.ToString(), user.Email, new { reason = "locked" });
            await uow.SaveChangesAsync(ct);
            return StaffErrors.AccountLocked(user.LockedUntil!.Value);
        }

        if (hasher.NeedsRehash(user.PassHash))
        {
            user.UpgradePasswordHash(hasher.Hash(command.Password));
            await uow.SaveChangesAsync(ct);
        }

        var mfa = issuer.IssueStaffMfa(user.Id);
        return new StaffLoginResponse(mfa.Token, user.TotpEnrolled);
    }
}

internal sealed class StaffLoginCommandValidator : AbstractValidator<StaffLoginCommand>
{
    public StaffLoginCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(c => c.Password).NotEmpty().MaximumLength(256);
    }
}
