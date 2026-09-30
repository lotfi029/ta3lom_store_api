using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.DTOs.Account;
using Ta3lom.Store.Application.Features.Enrolments.Commands.LaunchCourse;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/me` — the customer's account (11 §5). S-19: course launch (#29); the rest arrives with S-20 and S-31.
internal sealed class MeEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/me").WithTags("Me").RequireAuthorization(Policies.Customer).RequireRateLimiting(RatePolicies.Me);
        me.MapPost("/enrolments/{id:guid}/launch", LaunchAsync).Produces<LaunchResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> LaunchAsync(Guid id, HttpContext http, [FromServices] ICommandHandler<LaunchCourseCommand, LaunchResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new LaunchCourseCommand(id, http.Request.Headers.AcceptLanguage.ToString().StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? "en" : "ar"), ct)).ToHttp();
}
