using System.Globalization;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Api.Infrastructure;

/// Maps a Result to HTTP (DMS `CustomResults`): failures become a problem body with the stable `code`.
/// `type`, localised `title`/`detail` and `traceId` are filled by ProblemDetailsSetup. A 429 that carries
/// `retryAfterSeconds` also sets the `Retry-After` header.
public static class CustomResults
{
    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult>? ok = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? (ok?.Invoke(result.Value) ?? TypedResults.Ok(result.Value)) : result.Error.ToProblem();
    }

    public static IResult ToHttp(this Result result, Func<IResult>? ok = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? (ok?.Invoke() ?? TypedResults.NoContent()) : result.Error.ToProblem();
    }

    public static IResult ToProblem(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsFailure
            ? result.Error.ToProblem()
            : throw new InvalidOperationException("A successful result cannot be converted to a problem.");
    }

    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = error.Code };
        if (error.Extensions is not null)
        {
            foreach (var (key, value) in error.Extensions)
            {
                extensions[key] = value;
            }
        }

        var problem = TypedResults.Problem(title: error.Description, statusCode: error.Status, extensions: extensions);
        return error.Status == StatusCodes.Status429TooManyRequests && error.Extensions?.GetValueOrDefault("retryAfterSeconds") is int seconds
            ? new RetryAfterResult(problem, seconds)
            : problem;
    }

    /// The problem with a `Retry-After` header (D-10: when the next code may be requested).
    private sealed class RetryAfterResult(IResult inner, int seconds) : IResult, IStatusCodeHttpResult, IValueHttpResult
    {
        public int? StatusCode => (inner as IStatusCodeHttpResult)?.StatusCode;

        public object? Value => (inner as IValueHttpResult)?.Value;

        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            return inner.ExecuteAsync(httpContext);
        }
    }
}
