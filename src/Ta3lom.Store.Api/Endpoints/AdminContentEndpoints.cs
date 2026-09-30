using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.CreateCategory;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.UpdateCategory;
using Ta3lom.Store.Application.Features.AdminCatalog.Queries.ListAdminCategories;
using Ta3lom.Store.Application.Features.Media.Commands.UpdateMediaAlt;
using Ta3lom.Store.Application.Features.Media.Commands.UploadMedia;
using Ta3lom.Store.Application.Features.Media.Queries.ListMedia;
using Ta3lom.Store.Application.Features.Settings.Commands.UpdateSetting;
using Ta3lom.Store.Application.Features.Settings.Queries.ListSettings;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/admin/categories`, `/v1/admin/media`, `/v1/admin/settings` (11 §9.5, §9.6, §9.17; endpoints #72–#77, #118, #119).
internal sealed class AdminContentEndpoints : IEndpoint
{
    /// PDFs and book files up to 200 MB (11 §9.6), plus multipart overhead.
    public const long MaxUploadBytes = 210L * 1024 * 1024;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireRateLimiting(RatePolicies.Admin)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);

        var categories = admin.MapGroup("/categories").WithTags("Admin · Categories");
        categories.MapGet("/", ListCategoriesAsync).RequireAuthorization(Policies.StaffRead).Produces<IReadOnlyList<AdminCategoryResponse>>();
        categories.MapPost("/", CreateCategoryAsync).RequireAuthorization(Policies.StaffCatalogue).EvictsCatalogCache()
            .Produces<AdminCategoryResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);
        categories.MapPut("/{id:guid}", UpdateCategoryAsync).RequireAuthorization(Policies.StaffCatalogue).EvictsCatalogCache()
            .Produces<AdminCategoryResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        var media = admin.MapGroup("/media").WithTags("Admin · Media");
        media.MapPost("/", UploadAsync).RequireAuthorization(Policies.StaffCatalogue).DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxUploadBytes))
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<MediaAssetResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge).ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        media.MapGet("/", ListMediaAsync).RequireAuthorization(Policies.StaffRead).Produces<PagedResult<MediaAssetResponse>>();
        media.MapPatch("/{id:guid}", UpdateAltAsync).RequireAuthorization(Policies.StaffCatalogue).EvictsCatalogCache()
            .Produces<MediaAssetResponse>().ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        var settings = admin.MapGroup("/settings").WithTags("Admin · Settings");
        settings.MapGet("/", ListSettingsAsync).RequireAuthorization(Policies.StaffRead).Produces<IReadOnlyList<SettingResponse>>();
        settings.MapPut("/{key}", UpdateSettingAsync).RequireAuthorization(Policies.StaffAdmin).EvictsCatalogCache()
            .Produces<SettingResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> ListCategoriesAsync([FromServices] IQueryHandler<ListAdminCategoriesQuery, IReadOnlyList<AdminCategoryResponse>> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ListAdminCategoriesQuery(), ct)).ToHttp();

    private static async Task<IResult> CreateCategoryAsync([FromBody] CategoryInput input, [FromServices] ICommandHandler<CreateCategoryCommand, AdminCategoryResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new CreateCategoryCommand(input), ct)).ToHttp(c => TypedResults.Created($"/v1/admin/categories/{c.Id}", c));

    private static async Task<IResult> UpdateCategoryAsync(Guid id, [FromBody] CategoryInput input, [FromServices] ICommandHandler<UpdateCategoryCommand, AdminCategoryResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpdateCategoryCommand(id, input), ct)).ToHttp();

    private static async Task<IResult> UploadAsync(
        IFormFile file,
        [FromForm] string kind,
        [FromForm] string? altAr,
        [FromForm] string? altEn,
        [FromServices] ICommandHandler<UploadMediaCommand, MediaAssetResponse> handler,
        CancellationToken ct)
    {
        await using var content = file.OpenReadStream();
        var result = await handler.HandleAsync(new UploadMediaCommand(kind, file.ContentType, file.Length, content, altAr, altEn), ct);
        return result.ToHttp(m => TypedResults.Created($"/v1/admin/media/{m.Id}", m));
    }

    private static async Task<IResult> ListMediaAsync([FromServices] IQueryHandler<ListMediaQuery, PagedResult<MediaAssetResponse>> handler, CancellationToken ct,
        string? kind = null, int page = 1, int pageSize = 20) =>
        (await handler.HandleAsync(new ListMediaQuery(kind, page, pageSize), ct)).ToHttp();

    private static async Task<IResult> UpdateAltAsync(Guid id, [FromBody] MediaAltInput input, [FromServices] ICommandHandler<UpdateMediaAltCommand, MediaAssetResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpdateMediaAltCommand(id, input.AltAr, input.AltEn), ct)).ToHttp();

    private static async Task<IResult> ListSettingsAsync([FromServices] IQueryHandler<ListSettingsQuery, IReadOnlyList<SettingResponse>> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ListSettingsQuery(), ct)).ToHttp();

    private static async Task<IResult> UpdateSettingAsync(string key, [FromBody] SettingInput input, [FromServices] ICommandHandler<UpdateSettingCommand, SettingResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpdateSettingCommand(key, input.Value), ct)).ToHttp();
}
