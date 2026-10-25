using Ta3lom.Store.Application.DTOs.Staff;

namespace Ta3lom.Store.Application.Features.Staff.Commands.ResetStaffPassword;

/// `POST /v1/admin/staff/{id}/reset-password` (#126): new one-time password, sessions revoked.
public sealed record ResetStaffPasswordCommand(Guid Id) : ICommand<OneTimePasswordResponse>;

internal sealed class ResetStaffPasswordCommandHandler(
    IStaffUserRepository staff,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher hasher,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<ResetStaffPasswordCommand, OneTimePasswordResponse>
{
    public async Task<Result<OneTimePasswordResponse>> HandleAsync(ResetStaffPasswordCommand command, CancellationToken ct = default)
    {
        var user = await staff.GetByIdAsync(command.Id, ct);
        if (user is null)
        {
            return StaffErrors.NotFound;
        }

        var password = hasher.GenerateOneTimePassword();
        user.ResetPassword(hasher.Hash(password), clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        await refreshTokens.RevokeAllForSubjectAsync(TokenSubjectType.Staff, user.Id, clock.UtcNow, ct);
        return new OneTimePasswordResponse(password);
    }
}
