namespace Ta3lom.Store.SharedKernel;

/// An expected business outcome (DMS `Error`, store rule K-d): `Code` is a stable 11 §1.6 value,
/// `Status` the HTTP status the Api returns, `Extensions` extra problem members (e.g. `attemptsLeft`).
/// The localised `title`/`detail` come from the Api's ErrorMessages resources, keyed by `Code`;
/// `Description` is the English fallback used in logs and tests.
public record Error(string Code, string Description, int Status, IReadOnlyDictionary<string, object?>? Extensions = null)
{
    public const string ValidationCode = "validation.failed";

    public static Error None { get; } = new(string.Empty, string.Empty, 0);

    public static Error BadRequest(string code, string description) => new(code, description, 400);

    public static Error Unauthorized(string code, string description) => new(code, description, 401);

    public static Error Forbidden(string code, string description) => new(code, description, 403);

    public static Error NotFound(string code, string description) => new(code, description, 404);

    public static Error Conflict(string code, string description) => new(code, description, 409);

    public static Error Gone(string code, string description) => new(code, description, 410);

    public static Error TooLarge(string code, string description) => new(code, description, 413);

    public static Error UnsupportedMediaType(string code, string description) => new(code, description, 415);

    public static Error Unprocessable(string code, string description) => new(code, description, 422);

    public static Error Locked(string code, string description) => new(code, description, 423);

    public static Error RateLimited(string code, string description) => new(code, description, 429);

    public static Error Unavailable(string code, string description) => new(code, description, 503);

    /// 400 `validation.failed` with camelCase field → messages in the `errors` member (11 §1.6).
    public static ValidationError Validation(IReadOnlyDictionary<string, string[]> errors) => new(errors);

    /// The same error with extra problem members (e.g. `attemptsLeft`, `retryAfterSeconds`).
    public Error With(string key, object? value)
    {
        var extensions = Extensions is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(Extensions, StringComparer.Ordinal);
        extensions[key] = value;
        return this with { Extensions = extensions };
    }
}

/// Shape validation failures (DMS `ValidationError`): `errors` maps camelCase fields to messages.
public sealed record ValidationError : Error
{
    public ValidationError(IReadOnlyDictionary<string, string[]> errors)
        : base(ValidationCode, "Validation failed", 400, new Dictionary<string, object?>(StringComparer.Ordinal) { ["errors"] = errors })
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
