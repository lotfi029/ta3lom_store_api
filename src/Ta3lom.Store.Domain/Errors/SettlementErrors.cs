namespace Ta3lom.Store.Domain.Errors;

/// Settlement and staff order actions (11 §1.6, RUL-29, plan 14 §13.4).
public static class SettlementErrors
{
    public static Error CancelNotAllowed => Error.Conflict(ErrorCodes.OrderCancelNotAllowed, "Only pending_payment or payment_failed orders can be cancelled.");
}
