namespace Ta3lom.Store.Domain.Errors;

/// Staff sign-in and staff administration (BR-A11, D-12).
public static class StaffErrors
{
    /// Uniform for a wrong email or a wrong password (no account enumeration).
    public static Error InvalidCredentials => Error.Unauthorized(ErrorCodes.InvalidCredentials, "Email or password is wrong.");

    public static Error AccountLocked(DateTimeOffset lockedUntil) =>
        Error.Locked(ErrorCodes.AccountLocked, "The account is locked.").With("lockedUntil", lockedUntil);

    public static Error TotpRequired => Error.Unauthorized(ErrorCodes.TotpRequired, "The second factor is required.");

    public static Error TotpInvalid => Error.Unauthorized(ErrorCodes.TotpInvalid, "The authenticator code is wrong.");

    /// L-9: the stored secret cannot be decrypted (key ring lost); an admin resets the authenticator.
    public static Error TotpUnavailable => Error.Unavailable(ErrorCodes.TotpUnavailable, "The authenticator cannot be checked right now.");

    public static Error TotpNotEnrolled => Error.Unauthorized(ErrorCodes.TotpNotEnrolled, "No authenticator is enrolled.");

    public static Error TotpAlreadyEnrolled => Error.Conflict(ErrorCodes.TotpAlreadyEnrolled, "An authenticator is already enrolled; ask an admin to reset it.");

    /// The access token carries `pwd_change=true`: change the one-time password first.
    public static Error PasswordChangeRequired => Error.Forbidden(ErrorCodes.PasswordChangeRequired, "Change your password first.");

    /// D-12: `reasons` lists `too_short`, `common` or `same_as_current`.
    public static Error PasswordWeak(IReadOnlyList<string> reasons) =>
        Error.Unprocessable(ErrorCodes.PasswordWeak, "The new password does not meet the policy.").With("reasons", reasons);

    public static Error CurrentPasswordInvalid => Error.Unprocessable(ErrorCodes.CurrentPasswordInvalid, "The current password is wrong.");

    /// D-12: the last active admin cannot lose the role or be deactivated.
    public static Error LastAdmin => Error.Conflict(ErrorCodes.StaffLastAdmin, "The last active admin cannot be removed.");

    public static Error EmailTaken => Error.Conflict(ErrorCodes.StaffEmailTaken, "A staff user with this email exists.");

    public static Error NotFound => Error.NotFound(ErrorCodes.NotFound, "Staff user not found.");
}
