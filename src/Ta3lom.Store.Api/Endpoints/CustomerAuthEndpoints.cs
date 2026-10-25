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

/// `/v1/auth` — donor OTP sign-in and sessions (BR-A09, D-10, D-11, D-13).
internal sealed class CustomerAuthEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth").RequireSameOrigin();

        group.MapPost("/otp/request", RequestOtpAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RatePolicies.OtpRequest)
            .Produces<OtpRequestedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/otp/verify", VerifyOtpAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RatePolicies.OtpVerify)
            .Produces<SessionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RatePolicies.AuthRefresh)
            .Produces<AccessTokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/logout", LogoutAsync)
            .AllowAnonymous()
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
        [FromServices] CartCookie cartCookie,
        [FromServices] IClock clock,
        CancellationToken ct)
    {
        // The guest cart (t3s_cart) is merged into the donor's cart (BR-A07), then the cookie is dropped.
        var command = new VerifyOtpCommand(request.Mobile, request.Code, cartCookie.Read(http.Request), http.Request.Headers.UserAgent.ToString());
        var result = await handler.HandleAsync(command, ct);
        return result.ToHttp(session =>
        {
            AuthCookies.Set(http.Response, AuthCookies.CustomerRefresh, AuthCookies.CustomerRefreshPath, session.RefreshToken, session.RefreshExpiresAt, clock.UtcNow);
            if (command.GuestCartId is not null)
            {
                CartCookie.Delete(http.Response);
            }

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
