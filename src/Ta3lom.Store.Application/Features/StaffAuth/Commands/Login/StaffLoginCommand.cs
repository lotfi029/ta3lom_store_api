using FluentValidation;
using Ta3lom.Store.Application.DTOs.Staff;

namespace Ta3lom.Store.Application.Features.StaffAuth.Commands.Login;

/// Step 1 of staff sign-in (BR-A11, D-12): email + password → 5-minute MFA token. A wrong password, an unknown or inactive
/// account and a locked account all answer 401 `auth.invalid_credentials` (A-2: the lock state never tells whether the
/// password was right). Five failures within 15 minutes lock the account for one hour; failures are recorded on the
/// locked row (A-1). Every outcome is audited (BR-A13).
public sealed record StaffLoginCommand(string Email, string Password) : ICommand<StaffLoginResponse>;

internal sealed class StaffLoginCommandHandler(
    IStaffUserRepository staff,
    IPasswordHasher hasher,
    IAccessTokenIssuer issuer,
    StaffFailures failures,
    IAuditTrail audit,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<StaffLoginCommand, StaffLoginResponse>
{
    public async Task<Result<StaffLoginResponse>> HandleAsync(StaffLoginCommand command, CancellationToken ct = default)
    {
        var email = command.Email.Trim();
        var user = await staff.FindByEmailAsync(email, ct);
        if (user is null || !user.IsActive)
        {
            hasher.SimulateVerify(command.Password);
            audit.Record(AuditActions.StaffLoginFailed, "staff_user", user?.Id.ToString(), "anonymous", new { reason = user is null ? "unknown" : "inactive" });
            await uow.SaveChangesAsync(ct);
            return StaffErrors.InvalidCredentials;
        }

        // The hash is checked outside the transaction (it is slow); the row is locked only to record the outcome.
        var passwordOk = hasher.Verify(user.PassHash, command.Password);
        return await uow.ExecuteInTransactionAsync(token => passwordOk ? SucceedAsync(user, command.Password, token) : FailAsync(user, token),
            commitOnFailure: true, ct);
    }

    private async Task<Result<StaffLoginResponse>> FailAsync(Domain.Entities.Identity.StaffUser user, CancellationToken ct)
    {
        await failures.RecordAsync(user, AuditActions.StaffLoginFailed, "password", ct);
        return StaffErrors.InvalidCredentials;
    }

    private async Task<Result<StaffLoginResponse>> SucceedAsync(Domain.Entities.Identity.StaffUser user, string password, CancellationToken ct)
    {
        await staff.LockForUpdateAsync(user, ct);
        if (user.IsLocked(clock.UtcNow))
        {
            audit.Record(AuditActions.StaffLoginFailed, "staff_user", user.Id.ToString(), user.Email, new { reason = "locked" });
            return StaffErrors.InvalidCredentials;
        }

        if (hasher.NeedsRehash(user.PassHash))
        {
            user.UpgradePasswordHash(hasher.Hash(password));
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
