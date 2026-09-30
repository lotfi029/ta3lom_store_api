namespace Ta3lom.Store.Domain.Carts;

/// Stored as text with a CHECK list; wire values are snake_case.
public enum CartStatus
{
    Active,
    Converted,
    Merged,
    Expired,
}
