using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Api.Infrastructure;

/// Maps a Result to HTTP (DMS `CustomResults`, K-d): failures become the 11 §1.6 problem body.
/// `type`, localised `title`/`detail` and `traceId` are filled by ProblemDetailsSetup.
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

        return TypedResults.Problem(title: error.Description, statusCode: error.Status, extensions: extensions);
    }
}
