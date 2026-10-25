namespace Ta3lom.Store.Domain.Errors;

/// Donor OTP sign-in (BR-A09, D-10).
public static class OtpErrors
{
    public static Error MobileInvalid => Error.BadRequest(ErrorCodes.OtpMobileInvalid, "Not a Saudi mobile number.");

    public static Error Expired => Error.BadRequest(ErrorCodes.OtpExpired, "No open code, or the code is older than 5 minutes.");

    public static Error TooManyAttempts => Error.RateLimited(ErrorCodes.OtpTooManyAttempts, "Five wrong attempts on this code.");

    public static Error RateLimited(TimeSpan retryAfter) =>
        Error.RateLimited(ErrorCodes.OtpRateLimited, "Too many codes requested.")
            .With("retryAfterSeconds", Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)));

    public static Error Invalid(int attemptsLeft) =>
        Error.BadRequest(ErrorCodes.OtpInvalid, "Wrong code.").With("attemptsLeft", attemptsLeft);
}
