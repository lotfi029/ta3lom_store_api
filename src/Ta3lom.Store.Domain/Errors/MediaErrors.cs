namespace Ta3lom.Store.Domain.Errors;

/// Media library uploads and alt text.
public static class MediaErrors
{
    public static Error AltRequired => Error.Unprocessable(ErrorCodes.MediaAltRequired, "Images need altAr.");

    public static Error TypeNotAllowed => Error.UnsupportedMediaType(ErrorCodes.MediaTypeNotAllowed, "File type not allowed.");

    public static Error TooLarge(long maxBytes) => Error.TooLarge(ErrorCodes.MediaTooLarge, "File too large.").With("max", maxBytes);
}

/// Settings (typed allow-list).
public static class SettingErrors
{
    public static Error UnknownKey => Error.NotFound(ErrorCodes.SettingUnknownKey, "Unknown setting key.");

    public static Error InvalidValue => Error.Unprocessable(ErrorCodes.SettingInvalidValue, "Invalid value for this setting.");
}

/// `Idempotency-Key` handling.
public static class IdempotencyErrors
{
    public static Error KeyRequired => Error.BadRequest(ErrorCodes.IdempotencyKeyRequired, "The Idempotency-Key header is required.");

    public static Error KeyReused => Error.Unprocessable(ErrorCodes.IdempotencyKeyReused, "The key was used with a different body.");

    public static Error InProgress => Error.Conflict(ErrorCodes.IdempotencyInProgress, "A request with this key is still running.");
}
