using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.DTOs.Orders;
using Ta3lom.Store.Application.Features.Orders.Commands.CancelOrder;
using Ta3lom.Store.Application.Features.Orders.Commands.ResendDocuments;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/admin/orders` (11 §9.9). Cancel (#87, S-16), resend documents (#88, S-17); list and detail (#85, #86) arrive with S-21.
internal sealed class AdminOrderEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/orders").WithTags("Admin orders");
        group.MapPost("/{reference}/cancel", CancelAsync).RequireAuthorization(Policies.StaffFinance)
            .Produces<AdminOrderDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/{reference}/resend-documents", ResendAsync).RequireAuthorization(Policies.StaffResend)
            .Produces(StatusCodes.Status202Accepted).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ResendAsync(string reference, [FromBody] ResendDocumentsRequest request,
        [FromServices] ICommandHandler<ResendDocumentsCommand> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ResendDocumentsCommand(reference, request.Channels ?? []), ct)).ToHttp(() => TypedResults.Accepted((string?)null));

    private static async Task<IResult> CancelAsync(string reference, [FromBody] CancelOrderRequest request,
        [FromServices] ICommandHandler<CancelOrderCommand, AdminOrderDetailResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new CancelOrderCommand(reference, request.Reason ?? string.Empty), ct)).ToHttp();
}
