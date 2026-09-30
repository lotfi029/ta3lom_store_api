namespace Ta3lom.Store.Domain.Errors;

/// Checkout, orders and Idempotency-Key (11 §1.6, RUL-15, RUL-24 … RUL-30).
public static class CheckoutErrors
{
    public static Error CartEmpty => Error.Conflict(ErrorCodes.CheckoutCartEmpty, "There is nothing to pay for.");

    public static Error LinesInvalid(object lines) =>
        Error.Unprocessable(ErrorCodes.CheckoutLinesInvalid, "One or more lines fail re-validation.").With("lines", lines);

    public static Error PriceChanged(object quote) => Error.Conflict(ErrorCodes.CheckoutPriceChanged, "The repriced total differs.").With("quote", quote);

    public static Error OutOfStock(Guid bookFormatId) =>
        Error.Conflict(ErrorCodes.CheckoutOutOfStock, "The stock reservation failed.").With("bookFormatId", bookFormatId);

    public static Error AddressRequired => Error.Unprocessable(ErrorCodes.CheckoutAddressRequired, "A print line needs a shipping address.");

    public static Error ContactRequired => Error.Unprocessable(ErrorCodes.CheckoutContactRequired, "Name and email are required.");

    public static Error TermsNotAccepted => Error.Unprocessable(ErrorCodes.CheckoutTermsNotAccepted, "The terms must be accepted.");

    public static Error GatewayUnavailable(string reference, string statusToken) =>
        Error.Unavailable(ErrorCodes.CheckoutGatewayUnavailable, "The payment session could not be created.")
            .With("reference", reference).With("statusToken", statusToken);

    public static Error OrderNotFound => Error.NotFound(ErrorCodes.OrderNotFound, "Unknown reference or not the caller's order.");
}

/// Idempotency-Key filter (11 §1.5, 09 §5.16).
public static class IdempotencyErrors
{
    public static Error KeyRequired => Error.BadRequest(ErrorCodes.IdempotencyKeyRequired, "The Idempotency-Key header is required.");

    public static Error KeyReused => Error.Unprocessable(ErrorCodes.IdempotencyKeyReused, "The key was used with a different request.");

    public static Error InProgress => Error.Conflict(ErrorCodes.IdempotencyInProgress, "A request with this key is in progress.");
}
