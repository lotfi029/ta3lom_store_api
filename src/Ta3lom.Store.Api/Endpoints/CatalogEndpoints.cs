using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Catalog;
using Ta3lom.Store.Application.Features.Catalog.Queries;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/catalog` — the public catalogue (BR-A01…A06): anonymous, ETag + output cache, evicted by every catalogue write.
internal sealed class CatalogEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var catalog = app.MapGroup("/catalog").WithTags("Catalogue").AllowAnonymous().RequireRateLimiting(RatePolicies.Public);
        catalog.MapGet("/home", HomeAsync).CachedContent().Produces<HomeResponse>();
        catalog.MapGet("/categories", CategoriesAsync).CachedContent().Produces<IReadOnlyList<CategorySummary>>();
        catalog.MapGet("/config", ConfigAsync).CachedContent().Produces<CatalogConfigResponse>();
        catalog.MapGet("/projects", ProjectsAsync).CachedContent()
            .Produces<PagedResult<ProjectCard>>().Produces<ProjectSlugsResponse>().ProducesProblem(StatusCodes.Status400BadRequest);
        catalog.MapGet("/projects/{slug}", ProjectAsync).CachedContent()
            .Produces<ProjectDetail>().Produces<SlugRedirectResponse>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> HomeAsync([FromServices] IQueryHandler<GetHomeQuery, HomeResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetHomeQuery(), ct)).ToHttp();

    private static async Task<IResult> CategoriesAsync([FromServices] IQueryHandler<ListCategoriesQuery, IReadOnlyList<CategorySummary>> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ListCategoriesQuery(), ct)).ToHttp();

    private static async Task<IResult> ConfigAsync([FromServices] IQueryHandler<GetCatalogConfigQuery, CatalogConfigResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetCatalogConfigQuery(), ct)).ToHttp();

    private static async Task<IResult> ProjectsAsync(
        [FromServices] IQueryHandler<ListProjectsQuery, PagedResult<ProjectCard>> list,
        [FromServices] IQueryHandler<ListProjectSlugsQuery, ProjectSlugsResponse> slugs,
        CancellationToken ct,
        string? q = null, string? category = null, string? sort = null, int page = 1, int pageSize = 20, string? fields = null) =>
        fields is null
            ? (await list.HandleAsync(new ListProjectsQuery(q, category, sort, page, pageSize), ct)).ToHttp()
            : (await slugs.HandleAsync(new ListProjectSlugsQuery(fields), ct)).ToHttp();

    private static async Task<IResult> ProjectAsync(string slug, [FromServices] IQueryHandler<GetProjectBySlugQuery, ProjectLookup> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new GetProjectBySlugQuery(slug), ct))
            .ToHttp(found => found.Project is { } project ? TypedResults.Ok(project) : TypedResults.Ok(found.Redirect));
}
