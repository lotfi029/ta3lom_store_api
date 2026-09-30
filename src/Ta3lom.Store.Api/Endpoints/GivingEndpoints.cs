using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Giving;
using Ta3lom.Store.Application.Features.Giving;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// Dedication cards (#45 public, #93 resend) and cause updates (#81–#84) — 11 §8.1, §9.8, §9.11; RUL-22, RUL-54.
internal sealed class GivingEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/cards/{token}", CardAsync).WithTags("Cards").AllowAnonymous().RequireRateLimiting(RatePolicies.Public)
            .Produces<CardResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        var admin = app.MapGroup("/admin").RequireRateLimiting(RatePolicies.Admin).WithTags("Admin cause updates");
        admin.MapGet("/products/{id:guid}/updates", ListAsync).RequireAuthorization(Policies.StaffRead).Produces<PagedResult<AdminCauseUpdate>>();
        admin.MapPost("/products/{id:guid}/updates", PostAsync).RequireAuthorization(Policies.StaffCatalogue)
            .Produces<AdminCauseUpdate>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPut("/cause-updates/{updateId:guid}", EditAsync).RequireAuthorization(Policies.StaffCatalogue)
            .Produces<AdminCauseUpdate>().ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapDelete("/cause-updates/{updateId:guid}", DeleteAsync).RequireAuthorization(Policies.StaffCatalogue)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status409Conflict);
        admin.MapPost("/dedications/{id:guid}/resend", ResendAsync).WithTags("Admin orders").RequireAuthorization(Policies.StaffResend)
            .Produces(StatusCodes.Status202Accepted).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> CardAsync(string token, HttpContext http, [FromServices] IQueryHandler<GetCardQuery, CardResponse> handler, CancellationToken ct)
    {
        http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";                        // RUL-22: unlisted page
        http.Response.Headers.CacheControl = "private, no-store";
        return (await handler.HandleAsync(new GetCardQuery(token), ct)).ToHttp();
    }

    private static async Task<IResult> ListAsync(Guid id, [FromServices] IQueryHandler<ListCauseUpdatesQuery, PagedResult<AdminCauseUpdate>> handler,
        CancellationToken ct, int page = 1, int pageSize = 20) =>
        (await handler.HandleAsync(new ListCauseUpdatesQuery(id, page, pageSize), ct)).ToHttp();

    private static async Task<IResult> PostAsync(Guid id, [FromBody] CauseUpdateInput input,
        [FromServices] ICommandHandler<PostCauseUpdateCommand, AdminCauseUpdate> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new PostCauseUpdateCommand(id, input), ct)).ToHttp(u => TypedResults.Created($"/v1/admin/cause-updates/{u.Id}", u));

    private static async Task<IResult> EditAsync(Guid updateId, [FromBody] CauseUpdateInput input,
        [FromServices] ICommandHandler<EditCauseUpdateCommand, AdminCauseUpdate> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new EditCauseUpdateCommand(updateId, input), ct)).ToHttp();

    private static async Task<IResult> DeleteAsync(Guid updateId, [FromServices] ICommandHandler<DeleteCauseUpdateCommand> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new DeleteCauseUpdateCommand(updateId), ct)).ToHttp(() => TypedResults.NoContent());

    private static async Task<IResult> ResendAsync(Guid id, [FromBody] ResendDedicationRequest? request,
        [FromServices] ICommandHandler<ResendDedicationCommand> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new ResendDedicationCommand(id, request?.ToMobile), ct)).ToHttp(() => TypedResults.Accepted((string?)null));
}
