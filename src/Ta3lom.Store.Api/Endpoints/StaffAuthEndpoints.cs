using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Api.Security;
using Ta3lom.Store.Application.Abstractions.Messaging;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.DTOs.Staff;
using Ta3lom.Store.Application.Features.CustomerAuth.Commands.Logout;
using Ta3lom.Store.Application.Features.StaffAuth;
using Ta3lom.Store.Application.Features.StaffAuth.Commands.ChangePassword;
using Ta3lom.Store.Application.Features.StaffAuth.Commands.EnrolTotp;
using Ta3lom.Store.Application.Features.StaffAuth.Commands.Login;
using Ta3lom.Store.Application.Features.StaffAuth.Commands.RefreshStaffSession;
using Ta3lom.Store.Application.Features.StaffAuth.Commands.VerifyTotp;
using Ta3lom.Store.Application.Features.StaffAuth.Queries.GetStaffMe;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;

namespace Ta3lom.Store.Api.Endpoints;

/// `/v1/admin/auth` + `/v1/admin/me` — staff sign-in (BR-A11, D-11, D-12): password → TOTP → session; the password change
/// is the only route (with logout) open while a forced change is pending.
internal sealed class StaffAuthEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/admin/auth").WithTags("Admin · Auth").RequireSameOrigin();
        auth.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RatePolicies.StaffLogin)
            .Produces<StaffLoginResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status423Locked);
        auth.MapPost("/totp/enrol", EnrolAsync)
            .RequireAuthorization(AuthenticationSetup.StaffMfaPolicy)
            .RequireRateLimiting(RatePolicies.StaffLogin)
            .Produces<TotpEnrolResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);
        auth.MapPost("/totp/verify", VerifyAsync)
            .RequireAuthorization(AuthenticationSetup.StaffMfaPolicy)
            .RequireRateLimiting(RatePolicies.StaffLogin)
            .Produces<StaffSessionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status423Locked);
        auth.MapPost("/password", ChangePasswordAsync)
            .RequireAuthorization(AuthenticationSetup.StaffPasswordChangePolicy)
            .RequireRateLimiting(RatePolicies.StaffLogin)
            .Produces<StaffSessionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked);
        auth.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RatePolicies.AuthRefresh)
            .Produces<StaffSessionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);
        auth.MapPost("/logout", LogoutAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RatePolicies.AuthRefresh)
            .Produces(StatusCodes.Status204NoContent);

        app.MapGet("/admin/me", MeAsync)
            .WithTags("Admin · Auth")
            .RequireAuthorization(Policies.StaffRead)
            .RequireRateLimiting(RatePolicies.Admin)
            .Produces<StaffMeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] StaffLoginRequest request,
        [FromServices] ICommandHandler<StaffLoginCommand, StaffLoginResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new StaffLoginCommand(request.Email, request.Password), ct)).ToHttp();

    private static async Task<IResult> EnrolAsync(
        HttpContext http,
        [FromServices] ICommandHandler<EnrolTotpCommand, TotpEnrolResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new EnrolTotpCommand(http.User.SubjectId()), ct)).ToHttp();

    private static async Task<IResult> VerifyAsync(
        [FromBody] TotpVerifyRequest request,
        HttpContext http,
        [FromServices] ICommandHandler<VerifyTotpCommand, StaffSession> handler,
        [FromServices] IClock clock,
        CancellationToken ct)
    {
        var jti = http.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value ?? string.Empty;
        var expiresAt = long.TryParse(http.User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value, out var exp)
            ? DateTimeOffset.FromUnixTimeSeconds(exp)
            : clock.UtcNow;
        var command = new VerifyTotpCommand(http.User.SubjectId(), request.Code, jti, expiresAt, http.Request.Headers.UserAgent.ToString());
        return (await handler.HandleAsync(command, ct)).ToHttp(session => WithCookie(http, session, clock));
    }

    private static async Task<IResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request,
        HttpContext http,
        [FromServices] ICommandHandler<ChangePasswordCommand, StaffSession> handler,
        [FromServices] IClock clock,
        CancellationToken ct)
    {
        var command = new ChangePasswordCommand(http.User.SubjectId(), request.CurrentPassword, request.NewPassword, http.Request.Headers.UserAgent.ToString());
        return (await handler.HandleAsync(command, ct)).ToHttp(session => WithCookie(http, session, clock));
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext http,
        [FromServices] ICommandHandler<RefreshStaffSessionCommand, StaffSession> handler,
        [FromServices] IClock clock,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new RefreshStaffSessionCommand(http.Request.Cookies[AuthCookies.StaffRefresh], http.Request.Headers.UserAgent.ToString()), ct);
        if (result.IsFailure && result.Error.Code != ErrorCodes.RefreshRace)
        {
            AuthCookies.Clear(http.Response, AuthCookies.StaffRefresh, AuthCookies.StaffRefreshPath);
        }

        return result.ToHttp(session => WithCookie(http, session, clock));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext http,
        [FromServices] ICommandHandler<LogoutCommand> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new LogoutCommand(http.Request.Cookies[AuthCookies.StaffRefresh]), ct);
        AuthCookies.Clear(http.Response, AuthCookies.StaffRefresh, AuthCookies.StaffRefreshPath);
        return result.ToHttp();
    }

    private static async Task<IResult> MeAsync(
        HttpContext http,
        [FromServices] IQueryHandler<GetStaffMeQuery, StaffMeResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new GetStaffMeQuery(http.User.SubjectId()), ct)).ToHttp();

    private static Microsoft.AspNetCore.Http.HttpResults.Ok<StaffSessionResponse> WithCookie(HttpContext http, StaffSession session, IClock clock)
    {
        AuthCookies.Set(http.Response, AuthCookies.StaffRefresh, AuthCookies.StaffRefreshPath, session.RefreshToken, session.RefreshExpiresAt, clock.UtcNow);
        return TypedResults.Ok(session.Body);
    }
}
