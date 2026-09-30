namespace Ta3lom.Store.Domain.Errors;

/// Stable error codes (11 §1.6). Every constant must appear in 11 §1.6 (ErrorCodeCatalogueTests)
/// and have an Arabic and English message in Api/Resources/ErrorMessages*.resx (ErrorMessagesCompletenessTests).
/// Later sessions add the codes of their module here; the per-aggregate `XErrors` catalogues build `Error`s from them.
public static class ErrorCodes
{
    public const string ValidationFailed = "validation.failed";
    public const string NotFound = "http.not_found";
    public const string RateLimited = "http.rate_limited";
    public const string OriginForbidden = "http.origin_forbidden";
    public const string Unauthenticated = "auth.unauthenticated";
    public const string Forbidden = "auth.forbidden";
    public const string ConcurrencyConflict = "concurrency.conflict";
    public const string HttpError = "http.error";
    public const string ServerError = "server.error";
    public const string AccountBlocked = "auth.account_blocked";
    public const string RefreshInvalid = "auth.refresh_invalid";
    public const string RefreshExpired = "auth.refresh_expired";
    public const string RefreshReused = "auth.refresh_reused";
    public const string RefreshRace = "auth.refresh_race";
    public const string OtpMobileInvalid = "otp.mobile_invalid";
    public const string OtpRateLimited = "otp.rate_limited";
    public const string OtpInvalid = "otp.invalid";
    public const string OtpExpired = "otp.expired";
    public const string OtpTooManyAttempts = "otp.too_many_attempts";
}
