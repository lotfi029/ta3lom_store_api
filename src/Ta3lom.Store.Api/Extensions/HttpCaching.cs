using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Ta3lom.Store.Api.Extensions;

/// Public caching of the catalogue (11 §1.7): weak ETag = hash of the JSON body, `If-None-Match` → 304, and
/// `Cache-Control` per class (content: max-age 60 + SWR 300; live: max-age 15). Output caching keeps a copy per
/// URL (locale-free: the bodies are bilingual) and is evicted by tag when staff change the catalogue.
public static class HttpCaching
{
    public const string ContentPolicy = "catalog-content";
    public const string LivePolicy = "catalog-live";
    public const string CatalogTag = "catalog";

    /// The query keys the public catalogue reads; any other key shares the cached entry (no cache-busting by junk keys).
    public static readonly string[] VaryKeys = ["q", "category", "sort", "page", "pageSize", "fields"];

    public static IServiceCollection AddStoreOutputCache(this IServiceCollection services) =>
        services.AddOutputCache(o =>
        {
            o.AddPolicy(ContentPolicy, b => b.Expire(TimeSpan.FromSeconds(60)).SetVaryByQuery(VaryKeys).Tag(CatalogTag));
            o.AddPolicy(LivePolicy, b => b.Expire(TimeSpan.FromSeconds(15)).SetVaryByQuery(VaryKeys).Tag(CatalogTag));
        });

    /// Content endpoints: `public, max-age=60, stale-while-revalidate=300`.
    public static RouteHandlerBuilder CachedContent(this RouteHandlerBuilder builder) =>
        builder.CacheOutput(ContentPolicy).AddEndpointFilter(new ETagFilter("public, max-age=60, stale-while-revalidate=300"));

    /// Live figures: `public, max-age=15`.
    public static RouteHandlerBuilder CachedLive(this RouteHandlerBuilder builder) =>
        builder.CacheOutput(LivePolicy).AddEndpointFilter(new ETagFilter("public, max-age=15"));

    /// Admin catalogue writes evict the public catalogue cache (tag `catalog`) after a successful response.
    public static TBuilder EvictsCatalogCache<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => builder.AddEndpointFilter<TBuilder, EvictCatalogFilter>();

    private sealed class EvictCatalogFilter(Microsoft.AspNetCore.OutputCaching.IOutputCacheStore store) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var result = await next(context);
            var status = result is IStatusCodeHttpResult { StatusCode: { } code } ? code : StatusCodes.Status200OK;
            if (!HttpMethods.IsGet(context.HttpContext.Request.Method) && status is >= 200 and < 300)
            {
                await store.EvictByTagAsync(CatalogTag, context.HttpContext.RequestAborted);
            }

            return result;
        }
    }

    private sealed class ETagFilter(string cacheControl) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var result = await next(context);
            if (result is not IValueHttpResult { Value: { } value } || result is IStatusCodeHttpResult { StatusCode: not (null or 200) })
            {
                return result;
            }

            var http = context.HttpContext;
            var json = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
            var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), json));
            var etag = $"W/\"{Convert.ToHexStringLower(hash.AsSpan(0, 16))}\"";
            http.Response.Headers.ETag = etag;
            http.Response.Headers.CacheControl = cacheControl;

            var ifNoneMatch = http.Request.Headers.IfNoneMatch.ToString();
            return ifNoneMatch.Length > 0 && ifNoneMatch.Split(',').Any(t => t.Trim() == etag || t.Trim() == "*")
                ? TypedResults.StatusCode(StatusCodes.Status304NotModified)
                : result;
        }
    }
}
