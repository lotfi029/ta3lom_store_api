namespace Ta3lom.Store.Domain.Enums;

/// One payment attempt at the gateway (ADR-S05). Stored as snake_case text with a CHECK list.
/// initiated → captured | failed | cancelled; captured → refunded (full; partial keeps `captured` with
/// `refunded_amount > 0`). `review` = captured, but it could not settle the order (amount mismatch, late, double).
public enum PaymentStatus
{
    Initiated,
    Captured,
    Failed,
    Cancelled,
    Review,
    Refunded,
}
