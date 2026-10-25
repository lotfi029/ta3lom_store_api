using Ta3lom.Store.Application.DTOs.Staff;

namespace Ta3lom.Store.Application.Features.StaffAuth.Commands.EnrolTotp;

/// TOTP enrolment with the MFA token (D-12): allowed while no authenticator is enrolled, and again until the enrolment has
/// been confirmed by a first accepted code. Returns the otpauth URI and the base32 secret once.
public sealed record EnrolTotpCommand(Guid StaffId) : ICommand<TotpEnrolResponse>;

internal sealed class EnrolTotpCommandHandler(
    IStaffUserRepository staff,
    ITotpService totp,
    IAuditTrail audit,
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

        if (user.TotpVerified)
        {
            return StaffErrors.TotpAlreadyEnrolled;
        }

        var enrolment = totp.CreateEnrolment(user.Email);
        user.EnrolTotp(enrolment.ProtectedSecret);
        audit.Record(AuditActions.StaffTotpEnrolled, "staff_user", user.Id.ToString(), user.Email);        // L-5: the MFA token has no staff actor
        await uow.SaveChangesAsync(ct);
        return new TotpEnrolResponse(enrolment.OtpauthUri, enrolment.SecretBase32);
    }
}
