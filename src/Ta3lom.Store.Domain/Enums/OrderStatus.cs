namespace Ta3lom.Store.Domain.Enums;

/// Order lifecycle (D-09, ADR-S05). Stored as snake_case text with a CHECK list.
/// pending_payment → paid | payment_review | cancelled | expired; payment_review → paid | refunded;
/// paid → refunded (full refund; a partial refund keeps `paid` with `refunded_amount > 0`).
public enum OrderStatus
{
    PendingPayment,
    Paid,
    PaymentReview,
    Cancelled,
    Expired,
    Refunded,
}
