using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ta3lom.Store.Domain.Errors;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Api.Infrastructure;

/// DomainException → 409 with its code; a request the framework could not bind (malformed JSON, wrong body shape, oversize
/// body) → its 4xx status with `validation.failed` (400) or `http.error`; anything else → 500 `server.error`, no stack
/// trace (09 §5.14). Persistence races that pass the handlers' own checks: a stale `xmin` → 409 `concurrency.conflict`, a
/// unique violation (23505) → 409 `db.duplicate`, a foreign-key violation (23503) → 422 `db.reference_missing`.
internal sealed partial class GlobalExceptionHandler(IProblemDetailsService problems, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        int status;
        string code;
        if (exception is DomainException domain)
        {
            status = StatusCodes.Status409Conflict;
            code = domain.Code;
            LogDomainException(logger, domain.Code);
        }
        else if (exception is DbUpdateConcurrencyException)
        {
            status = StatusCodes.Status409Conflict;
            code = ErrorCodes.ConcurrencyConflict;
            LogPersistence(logger, code);
        }
        else if (Postgres(exception) is { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation } pg)
        {
            var duplicate = pg.SqlState == PostgresErrorCodes.UniqueViolation;
            status = duplicate ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity;
            code = duplicate ? ErrorCodes.Duplicate : ErrorCodes.ReferenceMissing;
            LogPersistence(logger, code);
        }
        else if (exception is BadHttpRequestException bad)
        {
            status = bad.StatusCode;
            code = status == StatusCodes.Status400BadRequest ? ErrorCodes.ValidationFailed : ErrorCodes.HttpError;
            LogBadRequest(logger, status);
        }
        else
        {
            status = StatusCodes.Status500InternalServerError;
            code = ErrorCodes.ServerError;
            LogUnhandled(logger, exception);
        }

        httpContext.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = status, Extensions = { ["code"] = code } },
        });
    }

    private static PostgresException? Postgres(Exception exception) =>
        exception as PostgresException ?? exception.InnerException as PostgresException;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Persistence conflict: {ErrorCode}")]
    private static partial void LogPersistence(ILogger logger, string errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Domain invariant violated: {ErrorCode}")]
    private static partial void LogDomainException(ILogger logger, string errorCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request body could not be bound ({Status})")]
    private static partial void LogBadRequest(ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
