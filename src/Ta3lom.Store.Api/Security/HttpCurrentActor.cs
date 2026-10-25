using System.Security.Claims;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Constants;

namespace Ta3lom.Store.Api.Security;

/// Who is calling, from the bearer token's claims; the IP is only ever exposed as an HMAC.
internal sealed class HttpCurrentActor(IHttpContextAccessor accessor, IPseudonymizer pseudonymizer) : ICurrentActor
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public ActorKind Kind => User?.Identity is { IsAuthenticated: true, AuthenticationType: var type }
        ? type switch
        {
            AuthSchemes.Staff => ActorKind.Staff,
            AuthSchemes.Customer => ActorKind.Customer,
            _ => ActorKind.Anonymous,          // the MFA step token is not an actor yet
        }
        : ActorKind.Anonymous;

    public Guid? Id => Guid.TryParse(User?.FindFirstValue("sub"), out var id) ? id : null;

    public string? Name => Kind == ActorKind.Staff ? User?.FindFirstValue("email") : null;

    public string? IpHash => accessor.HttpContext?.Connection.RemoteIpAddress is { } ip ? pseudonymizer.HashIp(ip.ToString()) : null;
}
