namespace Ta3lom.Store.Domain.Errors;

/// Cross-cutting errors (11 §1.6 "General"). Module catalogues (`CartErrors`, `CheckoutErrors` …) follow the same shape.
public static class GeneralErrors
{
    public static Error NotFound => Error.NotFound(ErrorCodes.NotFound, "The resource was not found.");

    public static Error Unauthenticated => Error.Unauthorized(ErrorCodes.Unauthenticated, "Authentication is required.");

    public static Error Forbidden => Error.Forbidden(ErrorCodes.Forbidden, "The caller may not perform this action.");

    public static Error ConcurrencyConflict => Error.Conflict(ErrorCodes.ConcurrencyConflict, "The resource was changed by someone else.");

    public static Error OriginForbidden => Error.Forbidden(ErrorCodes.OriginForbidden, "The request origin is not allowed.");
}
