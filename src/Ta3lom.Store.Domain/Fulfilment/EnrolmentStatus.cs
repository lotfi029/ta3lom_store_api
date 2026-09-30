namespace Ta3lom.Store.Domain.Fulfilment;

/// Stored as text with a CHECK list; wire values are snake_case.
public enum EnrolmentStatus
{
    PendingSync,
    Active,
    Completed,
    Revoked,
    Expired,
}
