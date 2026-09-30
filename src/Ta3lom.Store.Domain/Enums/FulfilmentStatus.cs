namespace Ta3lom.Store.Domain.Enums;

/// Stored as text with a CHECK list; wire values are snake_case.
public enum FulfilmentStatus
{
    Pending,
    Enrolled,
    PendingSync,
    Available,
    Preparing,
    Shipped,
    Delivered,
    Returned,
    Funded,
    Received,
    Refunded,
}
