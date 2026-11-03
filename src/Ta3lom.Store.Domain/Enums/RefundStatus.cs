namespace Ta3lom.Store.Domain.Enums;

/// A refund request to the gateway. Snake_case text with a CHECK list.
public enum RefundStatus
{
    Requested,
    Succeeded,
    Failed,
}
