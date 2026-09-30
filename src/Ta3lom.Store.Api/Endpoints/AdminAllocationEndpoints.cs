using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Allocation;
using Ta3lom.Store.Application.DTOs.Beneficiaries;
using Ta3lom.Store.Application.Features.Allocation.Commands;
using Ta3lom.Store.Application.Features.Allocation.Queries;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/admin/allocation`, `/v1/admin/units` (11 §9.14, #109–#114; RUL-47 … RUL-50). Allocation itself needs
/// `staff:allocate` (allocation_officer only, RUL-62).
internal sealed class AdminAllocationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/allocation").WithTags("Admin allocation");
        group.MapGet("/backlog", BacklogAsync).RequireAuthorization(Policies.StaffRead).Produces<IReadOnlyList<BacklogRow>>();
        group.MapGet("/products/{productId:guid}/units", UnitsAsync).RequireAuthorization(Policies.StaffBeneficiaries).Produces<PagedResult<UnitRow>>();
        group.MapGet("/products/{productId:guid}/candidates", CandidatesAsync).RequireAuthorization(Policies.StaffBeneficiaries)
            .Produces<PagedResult<BeneficiaryRow>>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/products/{productId:guid}/allocate", AllocateAsync).RequireAuthorization(Policies.StaffAllocate)
            .Produces<AllocationResult>().ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapGet("/batches/{batchId:guid}", BatchAsync).RequireAuthorization(Policies.StaffBeneficiaries)
            .Produces<AllocationBatchResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        app.MapPost("/admin/units/{unitId:guid}/complete", CompleteAsync).WithTags("Admin allocation").RequireAuthorization(Policies.StaffAllocate)
            .Produces<UnitRow>().ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> BacklogAsync([FromServices] IQueryHandler<AllocationBacklogQuery, IReadOnlyList<BacklogRow>> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new AllocationBacklogQuery(), ct)).ToHttp();

    private static async Task<IResult> UnitsAsync(Guid productId, [FromServices] IQueryHandler<ListUnitsQuery, PagedResult<UnitRow>> handler, CancellationToken ct,
        string? status = null, int page = 1, int pageSize = 20) =>
        (await handler.HandleAsync(new ListUnitsQuery(productId, status, page, pageSize), ct)).ToHttp();

    private static async Task<IResult> CandidatesAsync(Guid productId, [FromServices] IQueryHandler<ListCandidatesQuery, PagedResult<BeneficiaryRow>> handler,
        CancellationToken ct, string? region = null, bool zakatOnly = false, int page = 1, int pageSize = 20) =>
        (await handler.HandleAsync(new ListCandidatesQuery(productId, region, zakatOnly, page, pageSize), ct)).ToHttp();

    private static async Task<IResult> AllocateAsync(Guid productId, [FromBody] AllocateRequest request,
        [FromServices] ICommandHandler<AllocateUnitsCommand, AllocationResult> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new AllocateUnitsCommand(productId, request.BeneficiaryIds ?? [], request.Note), ct)).ToHttp();

    private static async Task<IResult> BatchAsync(Guid batchId, [FromServices] IQueryHandler<GetAllocationBatchQuery, AllocationBatchResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetAllocationBatchQuery(batchId), ct)).ToHttp();

    private static async Task<IResult> CompleteAsync(Guid unitId, [FromBody] CompleteUnitRequest request,
        [FromServices] ICommandHandler<CompleteUnitCommand, UnitRow> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new CompleteUnitCommand(unitId, request.DeliveredOn, request.ProofAssetId), ct)).ToHttp();
}
