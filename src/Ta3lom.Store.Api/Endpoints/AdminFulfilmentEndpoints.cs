using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.DTOs.Account;
using Ta3lom.Store.Application.Features.Downloads.Commands.ResetDownloadGrant;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/admin` fulfilment routes (11 §9.11–§9.12): download reset (#94, S-20); shipments and stock arrive with S-21.
internal sealed class AdminFulfilmentEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").WithTags("Admin fulfilment");
        admin.MapPost("/download-grants/{id:guid}/reset", ResetAsync).RequireAuthorization(Policies.StaffResend)
            .Produces<DownloadGrantResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status410Gone);
    }

    private static async Task<IResult> ResetAsync(Guid id, [FromBody] ResetDownloadGrantRequest request,
        [FromServices] ICommandHandler<ResetDownloadGrantCommand, DownloadGrantResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ResetDownloadGrantCommand(id, request.Reason ?? string.Empty), ct)).ToHttp();
}
