using Microsoft.Extensions.DependencyInjection;

namespace Ta3lom.Store.Domain;

public static class DependencyInjection
{
    /// Registers the domain services (DMS `AddDomain`; only DI abstractions are referenced).
    public static IServiceCollection AddDomain(this IServiceCollection services) => services;
}
