using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ta3lom.Store.Api.Endpoints;

namespace Ta3lom.Store.Api.Extensions;

public static class EndpointExtensions
{
    /// Registers every IEndpoint / IDevEndpoint class of the assembly (DMS `AddEndpoints`).
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var types = assembly.DefinedTypes.Where(t => t is { IsClass: true, IsAbstract: false }).ToList();
        services.TryAddEnumerable(types.Where(t => t.IsAssignableTo(typeof(IEndpoint))).Select(t => ServiceDescriptor.Transient(typeof(IEndpoint), t)));
        services.TryAddEnumerable(types.Where(t => t.IsAssignableTo(typeof(IDevEndpoint))).Select(t => ServiceDescriptor.Transient(typeof(IDevEndpoint), t)));
        return services;
    }

    /// Maps every registered IEndpoint onto <paramref name="group"/> (the `/v1` group). Resolved from DI,
    /// so tests can add probe modules with ConfigureTestServices.
    public static IEndpointRouteBuilder MapEndpoints(this WebApplication app, RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(app);
        foreach (var endpoint in app.Services.GetServices<IEndpoint>())
        {
            endpoint.MapEndpoint(group);
        }

        return app;
    }

    /// `/dev/*` only in Development and Staging (plan 14 §4.3).
    public static IEndpointRouteBuilder MapDevEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
        {
            var dev = app.MapGroup("/dev").WithTags("Dev").ExcludeFromDescription();
            foreach (var endpoint in app.Services.GetServices<IDevEndpoint>())
            {
                endpoint.MapEndpoint(dev);
            }
        }

        return app;
    }
}
