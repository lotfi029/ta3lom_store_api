namespace Ta3lom.Store.Application.Features.Staff.Commands.ResetStaffTotp;

/// `POST /v1/admin/staff/{id}/reset-totp`: the next sign-in enrols a new authenticator; every session is revoked.
public sealed record ResetStaffTotpCommand(Guid Id) : ICommand;

internal sealed class ResetStaffTotpCommandHandler(
    IStaffUserRepository staff,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<ResetStaffTotpCommand>
{
    public async Task<Result> HandleAsync(ResetStaffTotpCommand command, CancellationToken ct = default)
    {
        var user = await staff.GetByIdAsync(command.Id, ct);
        if (user is null)
        {
            return StaffErrors.NotFound;
        }

        user.ResetTotp();
        await uow.SaveChangesAsync(ct);
        await refreshTokens.RevokeAllForSubjectAsync(TokenSubjectType.Staff, user.Id, clock.UtcNow, ct);
        return Result.Success();
    }
}
