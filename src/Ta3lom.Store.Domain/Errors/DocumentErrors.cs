namespace Ta3lom.Store.Domain.Errors;

/// Order documents (11 §1.6, RUL-59).
public static class DocumentErrors
{
    public static Error NotFound => Error.NotFound(ErrorCodes.DocumentNotFound, "The document was not issued.");

    public static Error NotReady => Error.Conflict(ErrorCodes.DocumentNotReady, "The document is still rendering.");
}
