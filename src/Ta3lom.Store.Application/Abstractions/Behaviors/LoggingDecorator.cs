using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Ta3lom.Store.Application.Abstractions.Behaviors;

/// Logs the use-case name, outcome and duration. Never the payload (PII, 09 §8.1).
internal static partial class LoggingDecorator
{
    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        ILogger<CommandHandler<TCommand, TResponse>> logger)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            var started = Stopwatch.GetTimestamp();
            var result = await inner.HandleAsync(command, ct);
            LogOutcome(logger, "Command", typeof(TCommand).Name, result, started);
            return result;
        }
    }

    internal sealed class CommandHandler<TCommand>(
        ICommandHandler<TCommand> inner,
        ILogger<CommandHandler<TCommand>> logger)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            var started = Stopwatch.GetTimestamp();
            var result = await inner.HandleAsync(command, ct);
            LogOutcome(logger, "Command", typeof(TCommand).Name, result, started);
            return result;
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> inner,
        ILogger<QueryHandler<TQuery, TResponse>> logger)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct = default)
        {
            var started = Stopwatch.GetTimestamp();
            var result = await inner.HandleAsync(query, ct);
            LogOutcome(logger, "Query", typeof(TQuery).Name, result, started);
            return result;
        }
    }

    private static void LogOutcome(ILogger logger, string kind, string name, Result result, long started)
    {
        var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsSuccess)
        {
            LogSucceeded(logger, kind, name, elapsedMs);
        }
        else
        {
            LogFailed(logger, kind, name, result.Error.Code, elapsedMs);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} {UseCase} succeeded in {ElapsedMs:0.0} ms")]
    private static partial void LogSucceeded(ILogger logger, string kind, string useCase, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} {UseCase} failed with {ErrorCode} in {ElapsedMs:0.0} ms")]
    private static partial void LogFailed(ILogger logger, string kind, string useCase, string errorCode, double elapsedMs);
}
