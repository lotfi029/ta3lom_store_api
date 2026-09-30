namespace Ta3lom.Store.Domain.Errors;

/// Staff sign-in and staff administration (RUL-62, 11 §9.1, §9.19).
public static class StaffErrors
{
    /// Uniform for a wrong email or a wrong password (no account enumeration).
    public static Error InvalidCredentials => Error.Unauthorized(ErrorCodes.InvalidCredentials, "Email or password is wrong.");

    public static Error AccountLocked(DateTimeOffset lockedUntil) =>
        Error.Locked(ErrorCodes.AccountLocked, "The account is locked.").With("lockedUntil", lockedUntil);

    public static Error TotpRequired => Error.Unauthorized(ErrorCodes.TotpRequired, "The second factor is required.");

    public static Error TotpInvalid => Error.Unauthorized(ErrorCodes.TotpInvalid, "The authenticator code is wrong.");

    public static Error TotpNotEnrolled => Error.Unauthorized(ErrorCodes.TotpNotEnrolled, "No authenticator is enrolled.");

    public static Error EmailTaken => Error.Conflict(ErrorCodes.StaffEmailTaken, "A staff user with this email exists.");

    public static Error NotFound => Error.NotFound(ErrorCodes.NotFound, "Staff user not found.");
}
