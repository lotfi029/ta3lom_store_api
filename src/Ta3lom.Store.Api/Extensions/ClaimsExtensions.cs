using System.Security.Claims;

namespace Ta3lom.Store.Api.Extensions;

public static class ClaimsExtensions
{
    /// The `sub` claim of an authenticated caller (the policy guarantees it exists).
    public static Guid SubjectId(this ClaimsPrincipal user) =>
        Guid.TryParse(user?.FindFirstValue("sub"), out var id) ? id : throw new InvalidOperationException("No subject claim.");
}
