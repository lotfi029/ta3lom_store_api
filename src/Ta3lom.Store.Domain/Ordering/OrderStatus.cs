namespace Ta3lom.Store.Domain.Ordering;

/// Stored as text with a CHECK list; wire values are snake_case.
public enum OrderStatus
{
    PendingPayment,
    Paid,
    PaymentFailed,
    PaymentReview,
    Expired,
    Cancelled,
    Fulfilled,
    PartiallyRefunded,
    Refunded,
}
