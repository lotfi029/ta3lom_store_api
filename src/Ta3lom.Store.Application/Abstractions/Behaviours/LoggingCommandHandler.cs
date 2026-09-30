using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Ta3lom.Store.Application.Abstractions.Behaviours;

/// Logs the command name, outcome and duration. Never the command payload (PII, 09 §8.1).
internal sealed partial class LoggingCommandHandler<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    ILogger<LoggingCommandHandler<TCommand, TResult>> logger)
    : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct)
    {
        var name = typeof(TCommand).Name;
        var started = Stopwatch.GetTimestamp();
        var result = await inner.HandleAsync(command, ct);
        var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        if (result.IsSuccess)
        {
            LogSucceeded(logger, name, elapsedMs);
        }
        else
        {
            LogFailed(logger, name, result.Error!.Code, elapsedMs);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Command {Command} succeeded in {ElapsedMs:0.0} ms")]
    private static partial void LogSucceeded(ILogger logger, string command, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Command {Command} failed with {ErrorCode} in {ElapsedMs:0.0} ms")]
    private static partial void LogFailed(ILogger logger, string command, string errorCode, double elapsedMs);
}
