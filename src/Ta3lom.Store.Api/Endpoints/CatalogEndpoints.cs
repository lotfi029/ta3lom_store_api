using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Catalog;
using Ta3lom.Store.Application.Features.Catalog.Queries.GetCatalogConfig;
using Ta3lom.Store.Application.Features.Catalog.Queries.GetCategories;
using Ta3lom.Store.Application.Features.Catalog.Queries.GetGiftFeed;
using Ta3lom.Store.Application.Features.Catalog.Queries.GetHome;
using Ta3lom.Store.Application.Features.Catalog.Queries.GetLiveFigures;
using Ta3lom.Store.Application.Features.Catalog.Queries.GetProductBySlug;
using Ta3lom.Store.Application.Features.Catalog.Queries.GetProductUpdates;
using Ta3lom.Store.Application.Features.Catalog.Queries.ListProducts;
using Ta3lom.Store.Application.Features.Catalog.Queries.ListProductSlugs;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/catalog` — public catalogue (11 §2, endpoints #1–#7 and #9; FR-S01–S09). Anonymous, cacheable,
/// published products only (RUL-02), rate policy `public`.
internal sealed class CatalogEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/catalog").WithTags("Catalog").RequireRateLimiting(RatePolicies.Public);

        group.MapGet("/home", HomeAsync).CachedContent().Produces<HomeResponse>();
        group.MapGet("/categories", CategoriesAsync).CachedContent().Produces<IReadOnlyList<CategoryResponse>>();
        group.MapGet("/products", ProductsAsync).CachedContent()
            .Produces<PagedResult<ProductCardResponse>>().ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapGet("/products/{slug}", ProductAsync).CachedContent()
            .Produces<ProductDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/live", LiveAsync).CachedLive()
            .Produces<LiveFiguresResponse>().ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapGet("/products/{slug}/updates", UpdatesAsync).CachedContent()
            .Produces<PagedResult<CauseUpdateResponse>>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/products/{slug}/feed", FeedAsync).CachedLive()
            .Produces<IReadOnlyList<GiftFeedItem>>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/config", ConfigAsync).CachedContent().Produces<CatalogConfigResponse>();
    }

    private static async Task<IResult> HomeAsync([FromServices] IQueryHandler<GetHomeQuery, HomeResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetHomeQuery(), ct)).ToHttp();

    private static async Task<IResult> CategoriesAsync([FromServices] IQueryHandler<GetCategoriesQuery, IReadOnlyList<CategoryResponse>> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetCategoriesQuery(), ct)).ToHttp();

    /// `fields=slug` returns the slim prerender rows (11 §2.3), otherwise product cards.
    private static async Task<IResult> ProductsAsync(
        [FromServices] IQueryHandler<ListProductsQuery, PagedResult<ProductCardResponse>> cards,
        [FromServices] IQueryHandler<ListProductSlugsQuery, PagedResult<ProductSlugResponse>> slugs,
        CancellationToken ct,
        string? type = null, string? category = null, bool? sponsorable = null, string? q = null, string? sort = null,
        string? fields = null, int page = 1, int? pageSize = null)
    {
        if (string.Equals(fields, "slug", StringComparison.Ordinal))
        {
            return (await slugs.HandleAsync(new ListProductSlugsQuery(type, page, pageSize ?? 100), ct)).ToHttp();
        }

        return (await cards.HandleAsync(new ListProductsQuery(type, category, sponsorable, q, sort, page, pageSize ?? 20), ct)).ToHttp();
    }

    private static async Task<IResult> ProductAsync(string slug, [FromServices] IQueryHandler<GetProductBySlugQuery, ProductDetailResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetProductBySlugQuery(slug), ct)).ToHttp();

    private static async Task<IResult> LiveAsync([FromServices] IQueryHandler<GetLiveFiguresQuery, LiveFiguresResponse> handler, CancellationToken ct, string? slugs = null) =>
        (await handler.HandleAsync(new GetLiveFiguresQuery((slugs ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)), ct)).ToHttp();

    private static async Task<IResult> UpdatesAsync(string slug, [FromServices] IQueryHandler<GetProductUpdatesQuery, PagedResult<CauseUpdateResponse>> handler,
        CancellationToken ct, int page = 1, int pageSize = 20) =>
        (await handler.HandleAsync(new GetProductUpdatesQuery(slug, page, pageSize), ct)).ToHttp();

    private static async Task<IResult> FeedAsync(string slug, [FromServices] IQueryHandler<GetGiftFeedQuery, IReadOnlyList<GiftFeedItem>> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetGiftFeedQuery(slug), ct)).ToHttp();

    private static async Task<IResult> ConfigAsync([FromServices] IQueryHandler<GetCatalogConfigQuery, CatalogConfigResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetCatalogConfigQuery(), ct)).ToHttp();
}
