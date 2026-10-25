namespace Ta3lom.Store.Api.Extensions;

/// API security headers: set on every response, including error responses.
/// In Development (the only environment that maps them), /docs and /openapi get no CSP and no X-Frame-Options so Scalar can load.
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    private const string ApiCsp = "default-src 'none'; frame-ancestors 'none'";

    /// Everything with a cookie or bearer token is never cached.
    private static readonly string[] NoStorePrefixes =
        ["/v1/me", "/v1/auth", "/v1/admin", "/v1/checkout", "/v1/cart", "/v1/orders", "/v1/downloads"];

    private static readonly string[] DocsPrefixes = ["/docs", "/openapi"];

    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var docsExempt = environment.IsDevelopment() && DocsPrefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
        var noStore = NoStorePrefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";    // H-1: a JSON API needs none
            if (!docsExempt)
            {
                headers.XFrameOptions = "DENY";
                headers.ContentSecurityPolicy = ApiCsp;
            }

            if (noStore)
            {
                headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
