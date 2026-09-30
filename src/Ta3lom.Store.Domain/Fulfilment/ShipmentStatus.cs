namespace Ta3lom.Store.Domain.Fulfilment;

/// Stored as text with a CHECK list; wire values are snake_case.
public enum ShipmentStatus
{
    Preparing,
    Shipped,
    Delivered,
    Returned,
}
