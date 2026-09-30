using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Account;
using Ta3lom.Store.Application.DTOs.Fulfilment;
using Ta3lom.Store.Application.Features.Downloads.Commands.ResetDownloadGrant;
using Ta3lom.Store.Application.Features.Fulfilment;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/admin` fulfilment routes (11 §9.7, §9.11, §9.12): stock (#78–#80), download reset (#94), shipments (#95, #97–#99) and packing slip (#96).
internal sealed class AdminFulfilmentEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireRateLimiting(RatePolicies.Admin).WithTags("Admin fulfilment");
        admin.MapPost("/download-grants/{id:guid}/reset", ResetAsync).RequireAuthorization(Policies.StaffResend)
            .Produces<DownloadGrantResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status410Gone);

        // Stock (11 §9.7, #78–#80)
        admin.MapGet("/stock", (bool? lowOnly, [FromServices] IQueryHandler<ListStockQuery, IReadOnlyList<StockRow>> h, CancellationToken ct) =>
                Run(h.HandleAsync(new ListStockQuery(lowOnly ?? false), ct)))
            .RequireAuthorization(Policies.StaffRead).Produces<IReadOnlyList<StockRow>>();
        admin.MapGet("/stock/{formatId:guid}/movements", (Guid formatId, int? page, int? pageSize,
                [FromServices] IQueryHandler<ListStockMovementsQuery, PagedResult<StockMovementResponse>> h, CancellationToken ct) =>
                Run(h.HandleAsync(new ListStockMovementsQuery(formatId, page ?? 1, pageSize ?? 20), ct)))
            .RequireAuthorization(Policies.StaffRead).Produces<PagedResult<StockMovementResponse>>();
        admin.MapPost("/stock/{formatId:guid}/movements", async (Guid formatId, [FromBody] StockMovementRequest body,
                [FromServices] ICommandHandler<AddStockMovementCommand, StockRow> h, CancellationToken ct) =>
                (await h.HandleAsync(new AddStockMovementCommand(formatId, body.Delta, body.Reason ?? string.Empty, body.Note), ct))
                    .ToHttp(row => TypedResults.Created($"/v1/admin/stock/{formatId}/movements", row)))
            .RequireAuthorization(Policies.StaffStock).Produces<StockRow>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // Shipments (11 §9.12, #95–#99)
        admin.MapGet("/shipments", (string? status, int? page, int? pageSize,
                [FromServices] IQueryHandler<ListShipmentsQuery, PagedResult<ShipmentResponse>> h, CancellationToken ct) =>
                Run(h.HandleAsync(new ListShipmentsQuery(status, page ?? 1, pageSize ?? 20), ct)))
            .RequireAuthorization(Policies.StaffRead).Produces<PagedResult<ShipmentResponse>>();
        admin.MapGet("/shipments/{id:guid}/packing-slip", async (Guid id, [FromServices] IQueryHandler<PackingSlipQuery, PackingSlipFile> h, CancellationToken ct) =>
                (await h.HandleAsync(new PackingSlipQuery(id), ct)).ToHttp(f => TypedResults.File(f.Content, "application/pdf", f.FileName)))
            .RequireAuthorization(Policies.StaffFulfilment).Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPost("/shipments/{id:guid}/ship", async (Guid id, [FromBody] ShipRequest body,
                [FromServices] ICommandHandler<ShipCommand, ShipmentResponse> h, CancellationToken ct) =>
                (await h.HandleAsync(new ShipCommand(id, body.Carrier ?? string.Empty, body.TrackingNo ?? string.Empty), ct)).ToHttp())
            .RequireAuthorization(Policies.StaffFulfilment).Produces<ShipmentResponse>().ProducesProblem(StatusCodes.Status409Conflict);
        admin.MapPost("/shipments/{id:guid}/deliver", async (Guid id, [FromBody] DeliverRequest? body,
                [FromServices] ICommandHandler<DeliverCommand, ShipmentResponse> h, CancellationToken ct) =>
                (await h.HandleAsync(new DeliverCommand(id, body?.DeliveredOn), ct)).ToHttp())
            .RequireAuthorization(Policies.StaffFulfilment).Produces<ShipmentResponse>().ProducesProblem(StatusCodes.Status409Conflict);
        admin.MapPost("/shipments/{id:guid}/return", async (Guid id, [FromBody] ReturnShipmentRequest body,
                [FromServices] ICommandHandler<ReturnShipmentCommand, ShipmentResponse> h, CancellationToken ct) =>
                (await h.HandleAsync(new ReturnShipmentCommand(id, body.Note ?? string.Empty, body.Restock), ct)).ToHttp())
            .RequireAuthorization(Policies.StaffFulfilment).Produces<ShipmentResponse>().ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Run<T>(Task<Ta3lom.Store.SharedKernel.Result<T>> call) => (await call).ToHttp();

    private static async Task<IResult> ResetAsync(Guid id, [FromBody] ResetDownloadGrantRequest request,
        [FromServices] ICommandHandler<ResetDownloadGrantCommand, DownloadGrantResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ResetDownloadGrantCommand(id, request.Reason ?? string.Empty), ct)).ToHttp();
}
