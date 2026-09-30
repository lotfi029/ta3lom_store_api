namespace Ta3lom.Store.Domain.Errors;

/// Admin catalogue, media and settings (11 §9.3–§9.6, §9.17; RUL-01 … RUL-07).
public static class AdminCatalogErrors
{
    public static Error ProductNotFound => Error.NotFound(ErrorCodes.NotFound, "Product not found.");

    public static Error SlugTaken => Error.Conflict(ErrorCodes.ProductSlugTaken, "The slug is taken.");

    public static Error CategorySlugTaken => Error.Conflict(ErrorCodes.CategorySlugTaken, "The category slug is taken.");

    public static Error TypeLocked => Error.Conflict(ErrorCodes.ProductTypeLocked, "The type cannot change after the first order.");

    public static Error PublishIncomplete(IReadOnlyList<string> missing) =>
        Error.Unprocessable(ErrorCodes.ProductPublishIncomplete, "Required publishing fields are missing.").With("missing", missing);

    public static Error OfferActiveExists => Error.Conflict(ErrorCodes.OfferActiveExists, "The product already has an active offer.");

    public static Error OfferDatesInvalid => Error.BadRequest(ErrorCodes.OfferDatesInvalid, "endsAt must be after startsAt.");

    public static Error GeneralFundExists => Error.Conflict(ErrorCodes.GeneralFundExists, "Another cause is the general fund.");

    public static Error MediaAltRequired => Error.Unprocessable(ErrorCodes.MediaAltRequired, "Images need altAr.");

    public static Error MediaTypeNotAllowed => Error.UnsupportedMediaType(ErrorCodes.MediaTypeNotAllowed, "File type not allowed.");

    public static Error MediaTooLarge(long maxBytes) => Error.TooLarge(ErrorCodes.MediaTooLarge, "File too large.").With("max", maxBytes);

    public static Error SettingUnknownKey => Error.NotFound(ErrorCodes.SettingUnknownKey, "Unknown setting key.");

    public static Error SettingInvalidValue => Error.Unprocessable(ErrorCodes.SettingInvalidValue, "Invalid value for this setting.");
}
