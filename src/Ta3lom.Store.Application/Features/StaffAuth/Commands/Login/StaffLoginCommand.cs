using FluentValidation;
using Ta3lom.Store.Application.DTOs.Staff;

namespace Ta3lom.Store.Application.Features.StaffAuth.Commands.Login;

/// Step 1 of staff sign-in (RUL-62, 09 §5.8): email + password → 5-minute MFA token. A wrong email and a wrong
/// password give the same error; five failures lock the account for one hour.
public sealed record StaffLoginCommand(string Email, string Password) : ICommand<StaffLoginResponse>;

internal sealed class StaffLoginCommandHandler(
    IStaffUserRepository staff,
    IPasswordHasher hasher,
    IAccessTokenIssuer issuer,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<StaffLoginCommand, StaffLoginResponse>
{
    /// Verified when the email is unknown, so both paths take the same time.
    private const string DummyHash = "AQAAAAIAAYagAAAAEB7N1wQ9eY1LkWZ7hQ3bY2xw5l1c5b7m3m0j2yq9tK1g8m8Qx3Vd2dM9m3m9l0n1lA==";

    public async Task<Result<StaffLoginResponse>> HandleAsync(StaffLoginCommand command, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var user = await staff.FindByEmailAsync(command.Email.Trim(), ct);
        if (user is null || !user.IsActive)
        {
            hasher.Verify(DummyHash, command.Password);
            return StaffErrors.InvalidCredentials;
        }

        if (user.IsLocked(now))
        {
            return StaffErrors.AccountLocked(user.LockedUntil!.Value);
        }

        if (!hasher.Verify(user.PassHash, command.Password))
        {
            user.RecordFailure(now);
            await uow.SaveChangesAsync(ct);
            return StaffErrors.InvalidCredentials;
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
