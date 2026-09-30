using Ta3lom.Store.Application.Interfaces;

namespace Ta3lom.Store.Api.Security;

/// Anonymous until customer (S-04) and staff (S-05) authentication add claims.
internal sealed class HttpCurrentActor : ICurrentActor
{
    public ActorKind Kind => ActorKind.Anonymous;

    public Guid? Id => null;

    public string? Name => null;

    public string? IpHash => null;
}
