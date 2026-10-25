using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Errors;
using Ta3lom.Store.Infrastructure.Services.Security;

namespace Ta3lom.Store.Api.Security;

/// Three JWT bearer schemes: `Customer` (audience store-customer), `Staff` (store-staff) and `StaffMfa` (the TOTP step);
/// same issuer, ClockSkew 30 s, inbound claim mapping off. Every staff policy also requires that no forced password change
/// is pending (`pwd_change`), except <see cref="StaffPasswordChangePolicy"/>.
public static class AuthenticationSetup
{
    /// The 5-minute MFA bearer used by the TOTP enrol/verify steps.
    public const string StaffMfaPolicy = "staff-mfa";

    /// Any signed-in staff member, even with `pwd_change`: only the password change route uses it.
    public const string StaffPasswordChangePolicy = "staff-password-change";

    public static IServiceCollection AddStoreAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddJwtBearer(AuthSchemes.Customer)
            .AddJwtBearer(AuthSchemes.Staff)
            .AddJwtBearer(AuthSchemes.StaffMfa);
        services.AddOptions<JwtBearerOptions>(AuthSchemes.Customer).Configure<IOptions<JwtOptions>>((o, jwt) =>
            Configure(o, AuthSchemes.Customer, jwt.Value.Issuer, jwt.Value.CustomerAudience, jwt.Value.CustomerSigningKeyBase64, jwt.Value.KeyId));
        services.AddOptions<JwtBearerOptions>(AuthSchemes.Staff).Configure<IOptions<JwtOptions>>((o, jwt) =>
            Configure(o, AuthSchemes.Staff, jwt.Value.Issuer, jwt.Value.StaffAudience, jwt.Value.StaffSigningKeyBase64, jwt.Value.KeyId));
        services.AddOptions<JwtBearerOptions>(AuthSchemes.StaffMfa).Configure<IOptions<JwtOptions>>((o, jwt) =>
            Configure(o, AuthSchemes.StaffMfa, jwt.Value.Issuer, jwt.Value.StaffMfaAudience, jwt.Value.StaffSigningKeyBase64, jwt.Value.KeyId));

        var authorization = services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Customer, p => p.AddAuthenticationSchemes(AuthSchemes.Customer).RequireAuthenticatedUser())
            .AddPolicy(Policies.CustomerOptional, p => p.AddAuthenticationSchemes(AuthSchemes.Customer).RequireAssertion(ctx =>
                ctx.User.Identity?.IsAuthenticated == true || ctx.Resource is not HttpContext { Request.Headers.Authorization.Count: > 0 }))
            .AddPolicy(StaffMfaPolicy, p => p.AddAuthenticationSchemes(AuthSchemes.StaffMfa).RequireAuthenticatedUser())
            .AddPolicy(StaffPasswordChangePolicy, p => p.AddAuthenticationSchemes(AuthSchemes.Staff).RequireAuthenticatedUser());

        foreach (var (policy, roles) in Policies.StaffRoles)
        {
            authorization.AddPolicy(policy, p => p.AddAuthenticationSchemes(AuthSchemes.Staff).RequireAuthenticatedUser()
                .AddRequirements(new NoPendingPasswordChangeRequirement()).RequireRole(roles));
        }

        services.AddSingleton<IAuthorizationHandler, NoPendingPasswordChangeHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, StoreAuthorizationResultHandler>();
        return services;
    }

    private static void Configure(JwtBearerOptions o, string scheme, string issuer, string audience, string keyBase64, string keyId)
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(keyBase64)) { KeyId = keyId },
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            AuthenticationType = scheme,          // ICurrentActor tells staff from customers by this
            RoleClaimType = StoreClaims.Role,
        };
    }
}

/// The staff token must not carry `pwd_change=true` (forced password change pending).
public sealed class NoPendingPasswordChangeRequirement : IAuthorizationRequirement;

internal sealed class NoPendingPasswordChangeHandler : AuthorizationHandler<NoPendingPasswordChangeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, NoPendingPasswordChangeRequirement requirement)
    {
        if (!context.User.HasClaim(StoreClaims.PasswordChangeRequired, "true"))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// A 403 caused by a pending password change answers `auth.password_change_required`, so the admin app can open the
/// password form; everything else keeps the default (401/403 problem bodies from the status-code pages).
internal sealed class StoreAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<NoPendingPasswordChangeRequirement>().Any() == true)
        {
            await StaffErrors.PasswordChangeRequired.ToProblem().ExecuteAsync(context);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
