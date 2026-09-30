using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Giving;
using Ta3lom.Store.Application.Features.Giving;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// Goal adjustments (11 §9.2, #136–#137; RUL-46, FR-S58): sweeps and finance corrections.
internal sealed class AdminGoalEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/products/{id:guid}/goal-adjustments").WithTags("Admin goal adjustments").RequireAuthorization(Policies.StaffFinance);
        group.MapGet("/", ListAsync).Produces<PagedResult<GoalAdjustmentRow>>();
        group.MapPost("/", AddAsync).Produces<GoalCorrectionResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> ListAsync(Guid id, [FromServices] IQueryHandler<ListGoalAdjustmentsQuery, PagedResult<GoalAdjustmentRow>> handler,
        CancellationToken ct, int page = 1, int pageSize = 20) =>
        (await handler.HandleAsync(new ListGoalAdjustmentsQuery(id, page, pageSize), ct)).ToHttp();

    private static async Task<IResult> AddAsync(Guid id, [FromBody] GoalCorrectionInput input,
        [FromServices] ICommandHandler<AddGoalCorrectionCommand, GoalCorrectionResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new AddGoalCorrectionCommand(id, input), ct))
            .ToHttp(r => TypedResults.Created($"/v1/admin/products/{id}/goal-adjustments", r));
}
