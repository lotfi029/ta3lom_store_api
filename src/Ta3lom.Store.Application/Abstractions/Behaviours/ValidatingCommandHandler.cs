using FluentValidation;

namespace Ta3lom.Store.Application.Abstractions.Behaviours;

/// Runs every FluentValidation validator of the command before the handler (shape validation, 09 §3.3).
/// Failures become 400 `validation.failed` with camelCase field names in `errors`.
internal sealed class ValidatingCommandHandler<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IEnumerable<IValidator<TCommand>> validators)
    : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(command, ct);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
        {
            return await inner.HandleAsync(command, ct);
        }

        var errors = failures
            .GroupBy(f => ToCamelCase(f.PropertyName), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        return Error.Validation(errors);
    }

    private static string ToCamelCase(string propertyPath) =>
        string.Join('.', propertyPath.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}
