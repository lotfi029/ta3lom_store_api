namespace Ta3lom.Store.Domain.Enums;

/// R5-Core sets only pending_payment and cancelled; the rest arrive with payments. Stored as snake_case text with a CHECK list.
public enum OrderStatus
{
    PendingPayment,
    Paid,
    PaymentReview,
    Cancelled,
    Expired,
}
