namespace Ta3lom.Store.Api.Endpoints;

/// Adds endpoints to the `/v1` group (CORS policy `web`). Program maps every registered contributor.
/// None is registered in production today; integration tests use it for probe routes, so no probe
/// route ever ships in production code.
public interface IV1EndpointContributor
{
    void Map(RouteGroupBuilder v1);
}
