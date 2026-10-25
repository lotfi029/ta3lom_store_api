using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Media;
using Ta3lom.Store.Application.Features.Media.Commands.UpdateMediaAlt;
using Ta3lom.Store.Application.Features.Media.Commands.UploadMedia;
using Ta3lom.Store.Application.Features.Media.Queries.ListMedia;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/admin/media` — the media library (upload, list, alt text).
internal sealed class AdminMediaEndpoints : IEndpoint
{
    /// The multipart body: the largest file (a 20 MB PDF) plus the form fields.
    public const long MaxUploadBytes = 21L * 1024 * 1024;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var media = app.MapGroup("/admin/media").WithTags("Admin · Media").RequireRateLimiting(RatePolicies.Admin).RequireSameOrigin()
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        media.MapPost("/", UploadAsync).RequireAuthorization(Policies.StaffCatalogue).DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxUploadBytes))
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<MediaAssetResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge).ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        media.MapGet("/", ListAsync).RequireAuthorization(Policies.StaffRead).Produces<PagedResult<MediaAssetResponse>>();
        media.MapPatch("/{id:guid}", UpdateAltAsync).RequireAuthorization(Policies.StaffCatalogue).EvictsCatalogCache()
            .Produces<MediaAssetResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> UploadAsync(
        IFormFile file,
        [FromForm] string kind,
        [FromForm] string? altAr,
        [FromForm] string? altEn,
        [FromServices] ICommandHandler<UploadMediaCommand, MediaAssetResponse> handler,
        CancellationToken ct)
    {
        await using var content = file.OpenReadStream();
        var result = await handler.HandleAsync(new UploadMediaCommand(kind, file.Length, content, altAr, altEn), ct);
        return result.ToHttp(m => TypedResults.Created($"/v1/admin/media/{m.Id}", m));
    }

    private static async Task<IResult> ListAsync([FromServices] IQueryHandler<ListMediaQuery, PagedResult<MediaAssetResponse>> handler, CancellationToken ct,
        string? kind = null, int page = 1, int pageSize = 20) =>
        (await handler.HandleAsync(new ListMediaQuery(kind, page, pageSize), ct)).ToHttp();

    private static async Task<IResult> UpdateAltAsync(Guid id, [FromBody] MediaAltInput input,
        [FromServices] ICommandHandler<UpdateMediaAltCommand, MediaAssetResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UpdateMediaAltCommand(id, input.AltAr, input.AltEn), ct)).ToHttp();
}
