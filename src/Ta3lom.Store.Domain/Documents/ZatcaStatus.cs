namespace Ta3lom.Store.Domain.Documents;

/// Stored as text with a CHECK list; wire values are snake_case.
public enum ZatcaStatus
{
    NotRequired,
    Pending,
    Reported,
    Cleared,
    Rejected,
}
