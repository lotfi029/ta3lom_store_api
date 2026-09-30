namespace Ta3lom.Store.SharedKernel;

/// Outcome of a use case (DMS `Result`). Failure carries an <see cref="Error"/>; success carries <see cref="Error.None"/>.
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (isSuccess == (error != Error.None))
        {
            throw new ArgumentException("A success has no error and a failure has one.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<T> Success<T>(T value) => new(value, true, Error.None);

    public static Result<T> Failure<T>(Error error) => new(default, false, error);

#pragma warning disable CA2225 // implicit conversions are the point of Result; Success/Failure are the named alternatives
    public static implicit operator Result(Error error) => Failure(error);
#pragma warning restore CA2225
}

public class Result<TValue> : Result
{
    private readonly TValue? _value;

    protected internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    /// The value of a success; reading it on a failure is a bug and throws.
    public TValue Value => IsSuccess ? _value! : throw new InvalidOperationException("A failed result has no value.");

#pragma warning disable CA2225
    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);
#pragma warning restore CA2225
}
