namespace Ta3lom.Store.Domain.Common;

/// Thrown only for invariant violations that indicate a bug or a race (09 §3.2).
/// Expected business outcomes are returned as Application errors, not thrown.
public sealed class DomainException : Exception
{
    public DomainException(string code, string? message = null)
        : base(message ?? code)
    {
        Code = code;
    }

    public DomainException()
        : this("domain.error")
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "domain.error";
    }

    /// Stable machine code, returned as the ProblemDetails `code` (11 §1.6).
    public string Code { get; }
}
