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
    public const string InvalidCredentials = "auth.invalid_credentials";
    public const string AccountLocked = "auth.account_locked";
    public const string TotpRequired = "auth.totp_required";
    public const string TotpInvalid = "auth.totp_invalid";
    public const string TotpNotEnrolled = "auth.totp_not_enrolled";
    public const string StaffEmailTaken = "staff.email_taken";
    public const string ProductNotFound = "catalog.product_not_found";
    public const string ProductSlugTaken = "product.slug_taken";
    public const string CategorySlugTaken = "category.slug_taken";
    public const string ProductTypeLocked = "product.type_locked";
    public const string ProductPublishIncomplete = "product.publish_incomplete";
    public const string OfferActiveExists = "offer.active_exists";
    public const string OfferDatesInvalid = "offer.dates_invalid";
    public const string GeneralFundExists = "cause.general_fund_exists";
    public const string MediaAltRequired = "media.alt_required";
    public const string MediaTypeNotAllowed = "media.type_not_allowed";
    public const string MediaTooLarge = "media.too_large";
    public const string SettingUnknownKey = "setting.unknown_key";
    public const string SettingInvalidValue = "setting.invalid_value";
}
