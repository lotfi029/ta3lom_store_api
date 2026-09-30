namespace Ta3lom.Store.Application.Abstractions;

/// A use case that changes state. One transaction per command (09 §3.4).
public interface ICommand<TResult>;

/// A read-only use case.
public interface IQuery<TResult>;

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken ct);
}

/// Result value for commands that return nothing.
public readonly record struct Unit
{
    public static Unit Value { get; }
}
