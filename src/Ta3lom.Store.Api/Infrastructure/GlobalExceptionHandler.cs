using Microsoft.AspNetCore.Diagnostics;
using Ta3lom.Store.Domain.Errors;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Api.Infrastructure;

/// DomainException → 409 with its code; anything else → 500 `server.error`, no stack trace (09 §5.14).
/// EF Core concurrency and unique-violation mappings are added with persistence (S-02).
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Domain invariant violated: {ErrorCode}")]
    private static partial void LogDomainException(ILogger logger, string errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
