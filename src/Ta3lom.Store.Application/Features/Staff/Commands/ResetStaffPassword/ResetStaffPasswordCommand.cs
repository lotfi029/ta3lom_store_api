using Ta3lom.Store.Application.DTOs.Staff;

namespace Ta3lom.Store.Application.Features.Staff.Commands.ResetStaffPassword;

/// `POST /v1/admin/staff/{id}/reset-password`: a new one-time password (shown once, must be changed at the next sign-in);
/// every session of the member is revoked.
public sealed record ResetStaffPasswordCommand(Guid Id) : ICommand<OneTimePasswordResponse>;

internal sealed class ResetStaffPasswordCommandHandler(
    IStaffUserRepository staff,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher hasher,
    IAuditTrail audit,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<ResetStaffPasswordCommand, OneTimePasswordResponse>
{
    /// L-6: the new hash and the session revocation commit together (or not at all); audited `staff.password_reset`.
    public Task<Result<OneTimePasswordResponse>> HandleAsync(ResetStaffPasswordCommand command, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(token => ResetAsync(command, token), ct);

    private async Task<Result<OneTimePasswordResponse>> ResetAsync(ResetStaffPasswordCommand command, CancellationToken ct)
    {
        var user = await staff.GetByIdAsync(command.Id, ct);
        if (user is null)
        {
            return StaffErrors.NotFound;
        }

        var now = clock.UtcNow;
        var password = hasher.GenerateOneTimePassword();
        user.ResetPassword(hasher.Hash(password), now);
        await refreshTokens.RevokeAllForSubjectAsync(TokenSubjectType.Staff, user.Id, now, ct);
        audit.Record(AuditActions.StaffPasswordReset, "staff_user", user.Id.ToString());
        return new OneTimePasswordResponse(password);
    }
}
