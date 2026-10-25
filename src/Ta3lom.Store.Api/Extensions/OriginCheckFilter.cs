using Microsoft.Extensions.Options;
using Ta3lom.Store.Api.Options;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.Extensions;

/// Unsafe requests to cookie routes with a foreign `Origin` get `403 http.origin_forbidden`.
/// Requests without an Origin header (server-to-server, curl) are allowed: browsers always send one on POST.
internal sealed class OriginCheckFilter(IOptions<StoreOptions> options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method)
            && request.Headers.Origin is { Count: > 0 } origin
            && !options.Value.Cors.Origins.Contains(origin.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            return GeneralErrors.OriginForbidden.ToProblem();
        }

        return await next(context);
    }
}

public static class OriginCheckExtensions
{
    public static TBuilder RequireSameOrigin<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => builder.AddEndpointFilter<TBuilder, OriginCheckFilter>();
}
