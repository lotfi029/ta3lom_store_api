namespace Ta3lom.Store.Application.Abstractions;

/// How an error maps to HTTP (the Api owns the status codes).
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    RateLimited,
    Unprocessable,
    Gone,
}

/// An expected business outcome with a stable machine code from <see cref="ErrorCodes"/> (09 §3.2).
public sealed record Error(string Code, string Message, ErrorKind Kind, IReadOnlyDictionary<string, object?>? Extensions = null)
{
    public static Error Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new(ErrorCodes.ValidationFailed, "Validation failed", ErrorKind.Validation,
            new Dictionary<string, object?> { ["errors"] = errors });
}

public readonly record struct Result<T>
{
    private Result(T? value, Error? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public static Result<T> Ok(T value) => new(value, null);

    public static Result<T> Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, error);
    }

#pragma warning disable CA2225 // implicit conversions are the point of Result; Ok/Fail are the named alternatives
    public static implicit operator Result<T>(T value) => Ok(value);

    public static implicit operator Result<T>(Error error) => Fail(error);
#pragma warning restore CA2225
}
