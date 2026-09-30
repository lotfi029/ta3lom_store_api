using Ta3lom.Store.Application.DTOs.Staff;

namespace Ta3lom.Store.Application.Features.StaffAuth.Commands.EnrolTotp;

/// TOTP enrolment (RUL-62): allowed while no authenticator is enrolled, or while the enrolment was never
/// confirmed by a first successful login. Returns the otpauth URI and the base32 secret once.
public sealed record EnrolTotpCommand(Guid StaffId) : ICommand<TotpEnrolResponse>;

internal sealed class EnrolTotpCommandHandler(
    IStaffUserRepository staff,
    ITotpService totp,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<EnrolTotpCommand, TotpEnrolResponse>
{
    public async Task<Result<TotpEnrolResponse>> HandleAsync(EnrolTotpCommand command, CancellationToken ct = default)
    {
        var user = await staff.GetByIdAsync(command.StaffId, ct);
        if (user is null || !user.IsActive)
        {
            return StaffErrors.TotpRequired;
        }

        if (user.IsLocked(clock.UtcNow))
        {
            return StaffErrors.AccountLocked(user.LockedUntil!.Value);
        }

        if (user.TotpEnrolled && user.LastLoginAt is not null)
        {
            return AuthErrors.Forbidden;
        }

        var enrolment = totp.CreateEnrolment(user.Email);
        user.EnrolTotp(enrolment.ProtectedSecret);
        await uow.SaveChangesAsync(ct);
        return new TotpEnrolResponse(enrolment.OtpauthUri, enrolment.SecretBase32);
    }
}
