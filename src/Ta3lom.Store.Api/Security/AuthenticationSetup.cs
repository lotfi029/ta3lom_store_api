using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Infrastructure.Services.Security;

namespace Ta3lom.Store.Api.Security;

/// Two JWT bearer schemes (09 §5.8): `Customer` (audience store-customer) and `Staff` (store-staff);
/// same issuer, different keys, ClockSkew 30 s, inbound claim mapping off.
public static class AuthenticationSetup
{
    /// The 5-minute MFA bearer used by the TOTP enrol/verify steps.
    public const string StaffMfaPolicy = "staff-mfa";

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
            .AddPolicy(StaffMfaPolicy, p => p.AddAuthenticationSchemes(AuthSchemes.StaffMfa).RequireAuthenticatedUser());

        // Staff policies (11 §1.2): role sets from Policies.StaffRoles, including segregation of duties (RUL-62).
        foreach (var (policy, roles) in Policies.StaffRoles)
        {
            authorization.AddPolicy(policy, p => p.AddAuthenticationSchemes(AuthSchemes.Staff).RequireAuthenticatedUser().RequireRole(roles));
        }

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
            RoleClaimType = "role",
        };
    }
}
