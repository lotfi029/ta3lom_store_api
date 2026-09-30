using FluentValidation;
using FluentValidation.Results;

namespace Ta3lom.Store.Application.Abstractions.Behaviors;

/// Runs every FluentValidation validator of the command before the handler (shape validation, 09 §3.3).
/// Failures become 400 `validation.failed` with camelCase field names in `errors` (DMS nested-class style).
internal static class ValidationDecorator
{
    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        IEnumerable<IValidator<TCommand>> validators)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            var failures = await ValidateAsync(command, validators, ct);
            return failures.Count == 0 ? await inner.HandleAsync(command, ct) : CreateValidationError(failures);
        }
    }

    internal sealed class CommandHandler<TCommand>(
        ICommandHandler<TCommand> inner,
        IEnumerable<IValidator<TCommand>> validators)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            var failures = await ValidateAsync(command, validators, ct);
            return failures.Count == 0 ? await inner.HandleAsync(command, ct) : CreateValidationError(failures);
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> inner,
        IEnumerable<IValidator<TQuery>> validators)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct = default)
        {
            var failures = await ValidateAsync(query, validators, ct);
            return failures.Count == 0 ? await inner.HandleAsync(query, ct) : CreateValidationError(failures);
        }
    }

    private static async Task<List<ValidationFailure>> ValidateAsync<T>(T request, IEnumerable<IValidator<T>> validators, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, ct);
            failures.AddRange(result.Errors);
        }

        return failures;
    }

    private static ValidationError CreateValidationError(List<ValidationFailure> failures) =>
        Error.Validation(failures
            .GroupBy(f => ToCamelCase(f.PropertyName), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal));

    /// `Input.SeatPrice` → `seatPrice`: the wrapper property of a command (`Input`, `Request`) is not a body field (11 §1.6).
    private static string ToCamelCase(string propertyPath)
    {
        var parts = propertyPath.Split('.');
        if (parts.Length > 1 && parts[0] is "Input" or "Request")
        {
            parts = parts[1..];
        }

        return string.Join('.', parts.Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
    }
}
