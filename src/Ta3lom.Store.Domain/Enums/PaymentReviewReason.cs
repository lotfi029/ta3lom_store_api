namespace Ta3lom.Store.Domain.Enums;

/// Why a captured payment could not settle its order (RUL-29, RUL-35): finance accepts or refunds it.
public enum PaymentReviewReason
{
    AmountMismatch,
    LateCapture,
    DoublePayment,
}
