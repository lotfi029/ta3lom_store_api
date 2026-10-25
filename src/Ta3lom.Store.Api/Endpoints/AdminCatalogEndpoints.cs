using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Catalog;
using Ta3lom.Store.Application.Features.AdminCatalog.Categories;
using Ta3lom.Store.Application.Features.AdminCatalog.Projects;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/admin/categories` and `/v1/admin/projects` (BR-A01…A05, D-05…D-08): reads for every staff role, writes for admin and
/// store_manager. Every write evicts the public catalogue cache.
internal sealed class AdminCatalogEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var categories = app.MapGroup("/admin/categories").WithTags("Admin · Categories")
            .RequireRateLimiting(RatePolicies.Admin).RequireSameOrigin()
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        categories.MapGet("/", ListCategoriesAsync).RequireAuthorization(Policies.StaffRead).Produces<IReadOnlyList<AdminCategoryResponse>>();
        categories.MapPost("/", CreateCategoryAsync).RequireAuthorization(Policies.StaffCatalogue).EvictsCatalogCache()
            .Produces<AdminCategoryResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        categories.MapPut("/order", ReorderCategoriesAsync).RequireAuthorization(Policies.StaffCatalogue).EvictsCatalogCache()
            .Produces<IReadOnlyList<AdminCategoryResponse>>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        categories.MapPut("/{id:guid}", UpdateCategoryAsync).RequireAuthorization(Policies.StaffCatalogue).EvictsCatalogCache()
            .Produces<AdminCategoryResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        var projects = app.MapGroup("/admin/projects").WithTags("Admin · Projects")
            .RequireRateLimiting(RatePolicies.Admin).RequireSameOrigin()
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        projects.MapGet("/", ListProjectsAsync).RequireAuthorization(Policies.StaffRead)
            .Produces<PagedResult<AdminProjectRow>>().ProducesProblem(StatusCodes.Status400BadRequest);
        projects.MapPost("/", CreateProjectAsync).RequireAuthorization(Policies.StaffCatalogue).EvictsCatalogCache()
            .Produces<AdminProjectResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        projects.MapGet("/{id:guid}", GetProjectAsync).RequireAuthorization(Policies.StaffRead)
            .Produces<AdminProjectResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        projects.MapPut("/{id:guid}", UpdateProjectAsync).RequireAuthorization(Policies.StaffCatalogue).EvictsCatalogCache()
            .Produces<AdminProjectResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        projects.MapPut("/{id:guid}/options", ReplaceOptionsAsync).RequireAuthorization(Policies.StaffCatalogue).EvictsCatalogCache()
            .Produces<AdminProjectResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        projects.MapPost("/{id:guid}/publish", PublishAsync).RequireAuthorization(Policies.StaffCatalogue).EvictsCatalogCache()
            .Produces<AdminProjectResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        projects.MapPost("/{id:guid}/archive", ArchiveAsync).RequireAuthorization(Policies.StaffCatalogue).EvictsCatalogCache()
            .Produces<AdminProjectResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        projects.MapGet("/{id:guid}/preview", PreviewAsync).RequireAuthorization(Policies.StaffRead)
            .Produces<ProjectDetail>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListCategoriesAsync([FromServices] IQueryHandler<ListAdminCategoriesQuery, IReadOnlyList<AdminCategoryResponse>> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ListAdminCategoriesQuery(), ct)).ToHttp();

    private static async Task<IResult> CreateCategoryAsync([FromBody] CategoryInput input,
        [FromServices] ICommandHandler<CreateCategoryCommand, AdminCategoryResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new CreateCategoryCommand(input), ct)).ToHttp(c => TypedResults.Created($"/v1/admin/categories/{c.Id}", c));

    private static async Task<IResult> UpdateCategoryAsync(Guid id, [FromBody] CategoryInput input,
        [FromServices] ICommandHandler<UpdateCategoryCommand, AdminCategoryResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpdateCategoryCommand(id, input), ct)).ToHttp();

    private static async Task<IResult> ReorderCategoriesAsync([FromBody] CategoryOrderInput input,
        [FromServices] ICommandHandler<ReorderCategoriesCommand, IReadOnlyList<AdminCategoryResponse>> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ReorderCategoriesCommand(input.Items ?? []), ct)).ToHttp();

    private static async Task<IResult> ListProjectsAsync([FromServices] IQueryHandler<ListAdminProjectsQuery, PagedResult<AdminProjectRow>> handler,
        CancellationToken ct, string? status = null, Guid? category = null, string? q = null, int page = 1, int pageSize = 20) =>
        (await handler.HandleAsync(new ListAdminProjectsQuery(status, category, q, page, pageSize), ct)).ToHttp();

    private static async Task<IResult> CreateProjectAsync([FromBody] ProjectInput input,
        [FromServices] ICommandHandler<CreateProjectCommand, AdminProjectResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new CreateProjectCommand(input), ct)).ToHttp(p => TypedResults.Created($"/v1/admin/projects/{p.Id}", p));

    private static async Task<IResult> GetProjectAsync(Guid id, [FromServices] IQueryHandler<GetAdminProjectQuery, AdminProjectResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new GetAdminProjectQuery(id), ct)).ToHttp();

    private static async Task<IResult> UpdateProjectAsync(Guid id, [FromBody] ProjectInput input,
        [FromServices] ICommandHandler<UpdateProjectCommand, AdminProjectResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpdateProjectCommand(id, input), ct)).ToHttp();

    private static async Task<IResult> ReplaceOptionsAsync(Guid id, [FromBody] ProjectOptionsInput input,
        [FromServices] ICommandHandler<ReplaceProjectOptionsCommand, AdminProjectResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ReplaceProjectOptionsCommand(id, input), ct)).ToHttp();

    private static async Task<IResult> PublishAsync(Guid id, [FromServices] ICommandHandler<PublishProjectCommand, AdminProjectResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new PublishProjectCommand(id), ct)).ToHttp();

    private static async Task<IResult> ArchiveAsync(Guid id, [FromServices] ICommandHandler<ArchiveProjectCommand, AdminProjectResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ArchiveProjectCommand(id), ct)).ToHttp();

    private static async Task<IResult> PreviewAsync(Guid id, [FromServices] IQueryHandler<PreviewProjectQuery, ProjectDetail> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new PreviewProjectQuery(id), ct)).ToHttp();
}
