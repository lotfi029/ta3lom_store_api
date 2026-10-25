namespace Ta3lom.Store.Application.Features.Staff.Commands.ResetStaffTotp;

/// `POST /v1/admin/staff/{id}/reset-totp`: the next sign-in enrols a new authenticator; every session is revoked.
public sealed record ResetStaffTotpCommand(Guid Id) : ICommand;

internal sealed class ResetStaffTotpCommandHandler(
    IStaffUserRepository staff,
    IRefreshTokenRepository refreshTokens,
    IAuditTrail audit,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<ResetStaffTotpCommand>
{
    /// L-6: the reset and the session revocation commit together; audited `staff.totp_reset`.
    public Task<Result> HandleAsync(ResetStaffTotpCommand command, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(token => ResetAsync(command, token), ct);

    private async Task<Result> ResetAsync(ResetStaffTotpCommand command, CancellationToken ct)
    {
        var user = await staff.GetByIdAsync(command.Id, ct);
        if (user is null)
        {
            return StaffErrors.NotFound;
        }

        user.ResetTotp();
        await refreshTokens.RevokeAllForSubjectAsync(TokenSubjectType.Staff, user.Id, clock.UtcNow, ct);
        audit.Record(AuditActions.StaffTotpReset, "staff_user", user.Id.ToString());
        return Result.Success();
    }
}
