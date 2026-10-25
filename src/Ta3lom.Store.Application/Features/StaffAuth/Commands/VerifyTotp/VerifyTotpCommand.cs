using FluentValidation;
using MapsterMapper;
using Ta3lom.Store.Application.DTOs.Staff;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Application.Features.StaffAuth.Commands.VerifyTotp;

/// Step 2 of staff sign-in: the TOTP code (±1 step) → access token (store-staff, 10 min) + refresh (8 h).
public sealed record VerifyTotpCommand(Guid StaffId, string Code, string? UserAgent) : ICommand<StaffSession>;

internal sealed class VerifyTotpCommandHandler(
    IStaffUserRepository staff,
    IRefreshTokenRepository refreshTokens,
    ITotpService totp,
    IRefreshTokenCodec codec,
    IAccessTokenIssuer issuer,
    IMapper mapper,
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

        if (!totp.Verify(user.TotpSecretEnc, command.Code))
        {
            user.RecordFailure(now);
            await uow.SaveChangesAsync(ct);
            return user.IsLocked(now) ? StaffErrors.AccountLocked(user.LockedUntil!.Value) : StaffErrors.TotpInvalid;
        }

        user.RecordLogin(now);
        var (raw, hash) = codec.Create();
        var expiresAt = now + SessionPolicy.StaffFamilyLifetime;
        refreshTokens.Add(RefreshToken.Issue(TokenSubjectType.Staff, user.Id, hash, Guid.CreateVersion7(), expiresAt, command.UserAgent, now));
        await uow.SaveChangesAsync(ct);

        var access = issuer.IssueStaff(user.Id, user.Email, user.Roles);
        var body = new StaffSessionResponse(access.Token, access.ExpiresInSeconds, mapper.Map<StaffMeResponse>(user));
        return new StaffSession(body, raw, expiresAt);
    }
}

internal sealed class VerifyTotpCommandValidator : AbstractValidator<VerifyTotpCommand>
{
    public VerifyTotpCommandValidator() => RuleFor(c => c.Code).NotEmpty().Matches("^[0-9]{6}$").WithMessage("code must be 6 digits.");
}
