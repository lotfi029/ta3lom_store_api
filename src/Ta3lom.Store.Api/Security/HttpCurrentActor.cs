using Ta3lom.Store.Application.Abstractions;

namespace Ta3lom.Store.Api.Security;

/// Anonymous until customer (S-04) and staff (S-05) authentication add claims.
internal sealed class HttpCurrentActor : ICurrentActor
{
    public ActorKind Kind => ActorKind.Anonymous;

    public Guid? Id => null;

    public string? Name => null;
}
