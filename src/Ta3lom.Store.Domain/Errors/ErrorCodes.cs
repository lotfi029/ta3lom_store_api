namespace Ta3lom.Store.Domain.Errors;

/// Stable error codes, returned as the ProblemDetails `code`. Every constant has an Arabic and an English message in
/// Api/Resources/ErrorMessages*.resx (ErrorCodeCatalogueTests). The per-aggregate `XErrors` catalogues build `Error`s from them.
public static class ErrorCodes
{
    // General
    public const string ValidationFailed = "validation.failed";
    public const string NotFound = "http.not_found";
    public const string RateLimited = "http.rate_limited";
    public const string OriginForbidden = "http.origin_forbidden";
    public const string HttpError = "http.error";
    public const string ServerError = "server.error";
    public const string ConcurrencyConflict = "concurrency.conflict";
    public const string Duplicate = "db.duplicate";
    public const string ReferenceMissing = "db.reference_missing";

    // Sessions
    public const string Unauthenticated = "auth.unauthenticated";
    public const string Forbidden = "auth.forbidden";
    public const string AccountBlocked = "auth.account_blocked";
    public const string RefreshInvalid = "auth.refresh_invalid";
    public const string RefreshExpired = "auth.refresh_expired";
    public const string RefreshReused = "auth.refresh_reused";
    public const string RefreshRace = "auth.refresh_race";

    // Donor OTP
    public const string OtpMobileInvalid = "otp.mobile_invalid";
    public const string OtpRateLimited = "otp.rate_limited";
    public const string OtpInvalid = "otp.invalid";
    public const string OtpExpired = "otp.expired";
    public const string OtpTooManyAttempts = "otp.too_many_attempts";

    // Staff sign-in and staff administration
    public const string InvalidCredentials = "auth.invalid_credentials";
    public const string AccountLocked = "auth.account_locked";
    public const string TotpRequired = "auth.totp_required";
    public const string TotpInvalid = "auth.totp_invalid";
    public const string TotpNotEnrolled = "auth.totp_not_enrolled";
    public const string StaffEmailTaken = "staff.email_taken";

    // Media and settings
    public const string MediaAltRequired = "media.alt_required";
    public const string MediaTypeNotAllowed = "media.type_not_allowed";
    public const string MediaTooLarge = "media.too_large";
    public const string SettingUnknownKey = "setting.unknown_key";
    public const string SettingInvalidValue = "setting.invalid_value";

    // Catalogue
    public const string CategoryNotFound = "category.not_found";
    public const string CategorySlugTaken = "category.slug_taken";
    public const string ProjectNotFound = "project.not_found";
    public const string ProjectSlugTaken = "project.slug_taken";
    public const string ProjectPublishIncomplete = "project.publish_incomplete";
    public const string ProjectInvalidTransition = "project.invalid_transition";
    public const string MediaImageRequired = "media.image_required";

    // Donation cart (D-01 … D-05)
    public const string CartTargetUnavailable = "cart.target_unavailable";
    public const string CartOptionUnavailable = "cart.option_unavailable";
    public const string CartOpenAmountNotAllowed = "cart.open_amount_not_allowed";
    public const string CartAmountBelowMinimum = "cart.amount_below_minimum";
    public const string CartAmountAboveMaximum = "cart.amount_above_maximum";
    public const string CartQuantityLimit = "cart.quantity_limit";
    public const string CartTooManyLines = "cart.too_many_lines";
    public const string CartTotalExceeded = "cart.total_exceeded";
    public const string CartLineNotFound = "cart.line_not_found";

    // Checkout and orders (D-09)
    public const string CheckoutCartEmpty = "checkout.cart_empty";
    public const string CheckoutLinesInvalid = "checkout.lines_invalid";
    public const string CheckoutContactRequired = "checkout.contact_required";
    public const string OrderNotFound = "order.not_found";
    public const string OrderCancelNotAllowed = "order.cancel_not_allowed";

    // Idempotency and templates
    public const string IdempotencyKeyRequired = "idempotency.key_required";
    public const string IdempotencyKeyReused = "idempotency.key_reused";
    public const string IdempotencyInProgress = "idempotency.in_progress";
    public const string TemplateRenderFailed = "template.render_failed";
}
