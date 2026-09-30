namespace Ta3lom.Store.Domain.Payments;

/// Stored as text with a CHECK list; wire values are snake_case.
public enum PaymentStatus
{
    Initiated,
    Authorized,
    Captured,
    Failed,
    Voided,
    Refunded,
    PartiallyRefunded,
}
