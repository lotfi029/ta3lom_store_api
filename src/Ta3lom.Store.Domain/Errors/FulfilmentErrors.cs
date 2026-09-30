namespace Ta3lom.Store.Domain.Errors;

/// Stock and shipments (11 §1.6, RUL-30, RUL-40).
public static class FulfilmentErrors
{
    public static Error StockBelowReserved => Error.Unprocessable(ErrorCodes.StockBelowReserved, "stock_on_hand would drop below stock_reserved.");

    public static Error ShipmentInvalidTransition => Error.Conflict(ErrorCodes.ShipmentInvalidTransition, "The shipment cannot move to that state.");
}
