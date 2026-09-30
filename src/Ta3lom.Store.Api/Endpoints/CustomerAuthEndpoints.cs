using Microsoft.AspNetCore.Mvc;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.DTOs.CustomerAuth;
using Ta3lom.Store.Application.Features.CustomerAuth;
using Ta3lom.Store.Application.Features.CustomerAuth.Commands.Logout;
using Ta3lom.Store.Application.Features.CustomerAuth.Commands.LogoutAll;
using Ta3lom.Store.Application.Features.CustomerAuth.Commands.RefreshSession;
using Ta3lom.Store.Application.Features.CustomerAuth.Commands.RequestOtp;
using Ta3lom.Store.Application.Features.CustomerAuth.Commands.VerifyOtp;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/auth` — customer OTP sign-in and sessions (11 §4, endpoints #16–#20; RUL-60, RUL-61, FR-S21, FR-S28).
internal sealed class CustomerAuthEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth").RequireSameOrigin();

        group.MapPost("/otp/request", RequestOtpAsync)
            .RequireRateLimiting(RatePolicies.OtpRequest)
            .Produces<OtpRequestedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/otp/verify", VerifyOtpAsync)
            .RequireRateLimiting(RatePolicies.OtpVerify)
            .Produces<SessionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/refresh", RefreshAsync)
            .RequireRateLimiting(RatePolicies.AuthRefresh)
            .Produces<AccessTokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/logout", LogoutAsync)
            .RequireRateLimiting(RatePolicies.AuthRefresh)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/logout-all", LogoutAllAsync)
            .RequireAuthorization(Policies.Customer)
            .RequireRateLimiting(RatePolicies.AuthRefresh)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> RequestOtpAsync(
        [FromBody] RequestOtpRequest request,
        [FromServices] ICommandHandler<RequestOtpCommand, OtpRequestedResponse> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new RequestOtpCommand(request.Mobile, request.Locale), ct);
        return result.ToHttp(body => TypedResults.Json(body, statusCode: StatusCodes.Status202Accepted));
    }

    private static async Task<IResult> VerifyOtpAsync(
        [FromBody] VerifyOtpRequest request,
        HttpContext http,
        [FromServices] ICommandHandler<VerifyOtpCommand, CustomerSession> handler,
        [FromServices] IClock clock,
        CancellationToken ct)
    {
        // The guest cart id (t3s_cart) is unsealed and passed here from S-13 on.
        var command = new VerifyOtpCommand(request.Mobile, request.Code, GuestCartId: null, http.Request.Headers.UserAgent.ToString());
        var result = await handler.HandleAsync(command, ct);
        return result.ToHttp(session =>
        {
            AuthCookies.Set(http.Response, AuthCookies.CustomerRefresh, AuthCookies.CustomerRefreshPath, session.RefreshToken, session.RefreshExpiresAt, clock.UtcNow);
            return TypedResults.Ok(session.Body);
        });
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext http,
        [FromServices] ICommandHandler<RefreshSessionCommand, RotatedSession> handler,
        [FromServices] IClock clock,
        CancellationToken ct)
    {
        var raw = http.Request.Cookies[AuthCookies.CustomerRefresh];
        var result = await handler.HandleAsync(new RefreshSessionCommand(raw, http.Request.Headers.UserAgent.ToString()), ct);
        if (result.IsFailure && result.Error.Code != ErrorCodes.RefreshRace)
        {
            AuthCookies.Clear(http.Response, AuthCookies.CustomerRefresh, AuthCookies.CustomerRefreshPath);
        }

        return result.ToHttp(session =>
        {
            AuthCookies.Set(http.Response, AuthCookies.CustomerRefresh, AuthCookies.CustomerRefreshPath, session.RefreshToken, session.RefreshExpiresAt, clock.UtcNow);
            return TypedResults.Ok(session.Body);
        });
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext http,
        [FromServices] ICommandHandler<LogoutCommand> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new LogoutCommand(http.Request.Cookies[AuthCookies.CustomerRefresh]), ct);
        AuthCookies.Clear(http.Response, AuthCookies.CustomerRefresh, AuthCookies.CustomerRefreshPath);
        return result.ToHttp();
    }

    private static async Task<IResult> LogoutAllAsync(
        HttpContext http,
        [FromServices] ICommandHandler<LogoutAllCommand> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new LogoutAllCommand(http.User.SubjectId()), ct);
        AuthCookies.Clear(http.Response, AuthCookies.CustomerRefresh, AuthCookies.CustomerRefreshPath);
        return result.ToHttp();
    }
}
