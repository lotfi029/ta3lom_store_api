using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Fulfilment;
using Ta3lom.Store.Application.DTOs.Orders;
using Ta3lom.Store.Application.Features.Orders.Commands.CancelOrder;
using Ta3lom.Store.Application.Features.Orders.Commands.ResendDocuments;
using Ta3lom.Store.Application.Features.Orders.Queries;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/admin/orders` (11 §9.9): search (#85) and detail (#86, S-21), cancel (#87, S-16), resend documents (#88, S-17).
internal sealed class AdminOrderEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/orders").WithTags("Admin orders");
        group.MapGet("/", async (string? q, string? status, DateOnly? from, DateOnly? to, string? kind, int? page, int? pageSize,
                [FromServices] IQueryHandler<SearchOrdersQuery, PagedResult<AdminOrderRow>> h, CancellationToken ct) =>
                (await h.HandleAsync(new SearchOrdersQuery(q, status, from, to, kind, page ?? 1, pageSize ?? 20), ct)).ToHttp())
            .RequireAuthorization(Policies.StaffRead).Produces<PagedResult<AdminOrderRow>>();
        group.MapGet("/{reference}", async (string reference, [FromServices] IQueryHandler<GetAdminOrderQuery, AdminOrderDetailResponse> h, CancellationToken ct) =>
                (await h.HandleAsync(new GetAdminOrderQuery(reference), ct)).ToHttp())
            .RequireAuthorization(Policies.StaffRead).Produces<AdminOrderDetailResponse>().ProducesProblem(StatusCodes.Status404NotFound);
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
