using System.Runtime.CompilerServices;

namespace Ta3lom.Store.SharedKernel;

/// Invariant checks that throw DomainException with a stable code (`guard.*`).
public static class Guard
{
    public static T NotNull<T>(T? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        where T : class =>
        value ?? throw new DomainException("guard.null", $"{name} is required");

    public static string NotEmpty(string? value, [CallerArgumentExpression(nameof(value))] string? name = null) =>
        string.IsNullOrWhiteSpace(value) ? throw new DomainException("guard.empty", $"{name} is required") : value;

    public static Guid NotEmpty(Guid value, [CallerArgumentExpression(nameof(value))] string? name = null) =>
        value == Guid.Empty ? throw new DomainException("guard.empty", $"{name} is required") : value;

    public static int InRange(int value, int min, int max, [CallerArgumentExpression(nameof(value))] string? name = null) =>
        value < min || value > max
            ? throw new DomainException("guard.out_of_range", $"{name} must be between {min} and {max}")
            : value;

    public static decimal NotNegative(decimal value, [CallerArgumentExpression(nameof(value))] string? name = null) =>
        value < 0 ? throw new DomainException("guard.negative", $"{name} must not be negative") : value;

    public static void Against(bool condition, string code, string? message = null)
    {
        if (condition)
        {
            throw new DomainException(code, message);
        }
    }
}
