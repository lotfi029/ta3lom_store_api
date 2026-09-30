namespace Ta3lom.Store.Domain.Errors;

/// Settlement and staff order actions (11 §1.6, RUL-29, plan 14 §13.4).
public static class SettlementErrors
{
    /// Internal: a paid line kind has no settlement effect yet; never returned for a complete deployment.
    public static Error EffectMissing(string kind, string productType) =>
        Error.Internal(ErrorCodes.SettlementEffectMissing, $"No settlement effect handles {kind} lines of {productType} products.")
            .With("kind", kind).With("productType", productType);

    public static Error CancelNotAllowed => Error.Conflict(ErrorCodes.OrderCancelNotAllowed, "Only pending_payment or payment_failed orders can be cancelled.");
}
