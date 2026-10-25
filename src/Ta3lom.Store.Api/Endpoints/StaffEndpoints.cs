using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.DTOs.Staff;
using Ta3lom.Store.Application.Features.Staff.Commands.CreateStaff;
using Ta3lom.Store.Application.Features.Staff.Commands.ResetStaffPassword;
using Ta3lom.Store.Application.Features.Staff.Commands.ResetStaffTotp;
using Ta3lom.Store.Application.Features.Staff.Commands.UnlockStaff;
using Ta3lom.Store.Application.Features.Staff.Commands.UpdateStaff;
using Ta3lom.Store.Application.Features.Staff.Queries.ListStaff;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/admin/staff` — staff users and roles (BR-A12, D-12; admin only). Mutations are audited by the save interceptor.
internal sealed class StaffEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/staff").WithTags("Admin · Staff").RequireSameOrigin()
            .RequireAuthorization(Policies.StaffAdmin)
            .RequireRateLimiting(RatePolicies.Admin)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/", ListAsync).Produces<IReadOnlyList<StaffUserResponse>>();
        group.MapPost("/", CreateAsync).Produces<CreateStaffResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPut("/{id:guid}", UpdateAsync).Produces<StaffUserResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/{id:guid}/reset-password", ResetPasswordAsync).Produces<OneTimePasswordResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{id:guid}/reset-totp", ResetTotpAsync).Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{id:guid}/unlock", UnlockAsync).Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync([FromServices] IQueryHandler<ListStaffQuery, IReadOnlyList<StaffUserResponse>> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ListStaffQuery(), ct)).ToHttp();

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateStaffRequest request,
        [FromServices] ICommandHandler<CreateStaffCommand, CreateStaffResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new CreateStaffCommand(request.Email, request.Name, request.Roles ?? []), ct))
            .ToHttp(body => TypedResults.Created($"/v1/admin/staff/{body.Staff.Id}", body));

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateStaffRequest request,
        [FromServices] ICommandHandler<UpdateStaffCommand, StaffUserResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new UpdateStaffCommand(id, request.Name, request.Roles ?? [], request.IsActive, request.Version), ct)).ToHttp();

    private static async Task<IResult> ResetPasswordAsync(Guid id, [FromServices] ICommandHandler<ResetStaffPasswordCommand, OneTimePasswordResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ResetStaffPasswordCommand(id), ct)).ToHttp();

    private static async Task<IResult> ResetTotpAsync(Guid id, [FromServices] ICommandHandler<ResetStaffTotpCommand> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ResetStaffTotpCommand(id), ct)).ToHttp();

    private static async Task<IResult> UnlockAsync(Guid id, [FromServices] ICommandHandler<UnlockStaffCommand> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new UnlockStaffCommand(id), ct)).ToHttp();
}
