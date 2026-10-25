using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using Ta3lom.Store.Api.Options;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.Infrastructure;

/// RFC 9457 ProblemDetails with a stable `code`: `type` = {PublicBaseUrl}/problems/{code},
/// `title`/`detail` in the request language (ar default, en), `traceId` = W3C trace id.
public static class ProblemDetailsSetup
{
    public static IServiceCollection AddStoreProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(o => o.CustomizeProblemDetails = Enrich);
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }

    private static void Enrich(ProblemDetailsContext ctx)
    {
        var problem = ctx.ProblemDetails;
        var status = problem.Status ?? StatusCodes.Status500InternalServerError;
        problem.Status = status;

        if (problem.Extensions.TryGetValue("code", out var existing) is false || existing is not string code || code.Length == 0)
        {
            code = DefaultCode(status);
            problem.Extensions["code"] = code;
        }

        var baseUrl = ctx.HttpContext.RequestServices.GetRequiredService<IOptions<StoreOptions>>().Value.PublicBaseUrl.TrimEnd('/');
        problem.Type = $"{baseUrl}/problems/{code}";

        var culture = RequestCulture(ctx.HttpContext);
        problem.Title = ErrorMessages.Title(code, culture) ?? problem.Title;
        problem.Detail = ErrorMessages.Detail(code, culture) ?? problem.Detail;
        problem.Extensions["traceId"] = (Activity.Current?.TraceId ?? ActivityTraceId.CreateRandom()).ToHexString();

        // Never leak internals: the framework's own exception fields are removed.
        problem.Extensions.Remove("exception");
        problem.Extensions.Remove("requestId");
    }

    /// The culture chosen by UseRequestLocalization. The feature survives the exception handler and
    /// status-code pages, which run outside the localisation middleware.
    private static CultureInfo RequestCulture(HttpContext http) =>
        http.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture ?? ErrorMessages.Arabic;

    private static string DefaultCode(int status) => status switch
    {
        400 => ErrorCodes.ValidationFailed,
        401 => ErrorCodes.Unauthenticated,
        403 => ErrorCodes.Forbidden,
        404 => ErrorCodes.NotFound,
        429 => ErrorCodes.RateLimited,
        >= 500 => ErrorCodes.ServerError,
        _ => ErrorCodes.HttpError,
    };
}
