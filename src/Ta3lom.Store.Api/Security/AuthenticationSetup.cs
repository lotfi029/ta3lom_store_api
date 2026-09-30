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
    public static IServiceCollection AddStoreAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddJwtBearer(AuthSchemes.Customer)
            .AddJwtBearer(AuthSchemes.Staff);

        services.AddOptions<JwtBearerOptions>(AuthSchemes.Customer).Configure<IOptions<JwtOptions>>((o, jwt) =>
            Configure(o, jwt.Value.Issuer, jwt.Value.CustomerAudience, jwt.Value.CustomerSigningKeyBase64, jwt.Value.KeyId));
        services.AddOptions<JwtBearerOptions>(AuthSchemes.Staff).Configure<IOptions<JwtOptions>>((o, jwt) =>
            Configure(o, jwt.Value.Issuer, jwt.Value.StaffAudience, jwt.Value.StaffSigningKeyBase64, jwt.Value.KeyId));

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Customer, p => p.AddAuthenticationSchemes(AuthSchemes.Customer).RequireAuthenticatedUser());
        return services;
    }

    private static void Configure(JwtBearerOptions o, string issuer, string audience, string keyBase64, string keyId)
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
            RoleClaimType = "role",
        };
    }
}
