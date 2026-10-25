namespace Ta3lom.Store.Domain.Errors;

/// Sessions and account state (RUL-60, RUL-61, RUL-62; 11 §1.6).
public static class AuthErrors
{
    public static Error AccountBlocked => Error.Forbidden(ErrorCodes.AccountBlocked, "The customer account is blocked.");

    public static Error RefreshInvalid => Error.Unauthorized(ErrorCodes.RefreshInvalid, "Unknown refresh token.");

    public static Error RefreshExpired => Error.Unauthorized(ErrorCodes.RefreshExpired, "The refresh token has expired.");

    public static Error RefreshReused => Error.Unauthorized(ErrorCodes.RefreshReused, "A rotated refresh token was reused; the family was revoked.");

    public static Error RefreshRace => Error.Conflict(ErrorCodes.RefreshRace, "Concurrent refresh; retry once.");
}
