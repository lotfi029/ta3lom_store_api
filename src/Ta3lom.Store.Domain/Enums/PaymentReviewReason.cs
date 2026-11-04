namespace Ta3lom.Store.Domain.Enums;

/// Why a captured payment could not settle its order (RUL-29, RUL-35): finance accepts or refunds it.
public enum PaymentReviewReason
{
    AmountMismatch,
    LateCapture,
    DoublePayment,

    /// The gateway shows a refund on a payment we never settled (review 04 A-1): never paid automatically.
    ExternalRefund,

    /// The gateway voided a payment we had already taken as captured (review 04 A-1).
    VoidedAfterCapture,
}
