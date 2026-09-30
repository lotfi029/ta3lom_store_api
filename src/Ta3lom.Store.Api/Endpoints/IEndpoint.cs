namespace Ta3lom.Store.Api.Endpoints;

/// One module of routes (DMS `IEndpoint`). Mapped onto the `/v1` group by <c>MapEndpoints</c>;
/// each module adds its own sub-group, tags and policies. Endpoints bind → call one handler → `ToHttp()`.
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}

/// Development/Staging-only routes (`/dev/*`, plan 14 §4.3). Never mapped in Production.
public interface IDevEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
