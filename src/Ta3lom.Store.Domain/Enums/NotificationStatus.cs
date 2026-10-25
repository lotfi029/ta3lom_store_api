namespace Ta3lom.Store.Domain.Enums;

/// Stored as text with a CHECK list; wire values are snake_case.
public enum NotificationStatus
{
    Sent,

    /// The provider refused it; the outbox may try again.
    Failed,

    /// Claimed and handed to the provider; the outcome is not recorded yet.
    Sending,

    /// Found still `sending` on a later attempt: the outcome is unknown, so it is never sent again (A-2).
    Unknown,
}
