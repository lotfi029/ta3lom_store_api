using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Features.Downloads.Commands.DownloadFile;
using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Api.Endpoints;

/// `GET /v1/downloads/{token}` (#46, 11 §8.2, RUL-41): the signed link is the credential (no bearer; a browser opens it).
internal sealed class DownloadEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/downloads/{token}", DownloadAsync).WithTags("Downloads").RequireRateLimiting(RatePolicies.Download)
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status410Gone);

    private static async Task<IResult> DownloadAsync(string token, [FromServices] ICommandHandler<DownloadFileCommand, DownloadedFile> handler,
        [FromServices] ICurrentActor actor, HttpContext http, CancellationToken ct)
    {
        var result = await handler.HandleAsync(new DownloadFileCommand(token, actor.IpHash), ct);
        return result.ToHttp(file =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.File(file.Content, "application/pdf", file.FileName);
        });
    }
}
