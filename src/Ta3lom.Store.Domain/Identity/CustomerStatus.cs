namespace Ta3lom.Store.Domain.Identity;

/// Stored as text with a CHECK list; wire values are snake_case.
public enum CustomerStatus
{
    Active,
    Blocked,
    Anonymised,
}
