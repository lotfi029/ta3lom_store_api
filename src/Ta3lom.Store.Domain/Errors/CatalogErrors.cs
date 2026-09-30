namespace Ta3lom.Store.Domain.Errors;

/// Public catalogue (11 §2, RUL-02).
public static class CatalogErrors
{
    public static Error ProductNotFound => Error.NotFound(ErrorCodes.ProductNotFound, "Slug unknown or not published.");
}
