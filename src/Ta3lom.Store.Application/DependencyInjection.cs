using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Application.Abstractions;
using Ta3lom.Store.Application.Abstractions.Behaviours;

namespace Ta3lom.Store.Application;

public static class DependencyInjection
{
    /// Registers every handler and validator of this assembly, then wraps command handlers:
    /// Logging → Validating → handler. TryDecorate is used because Decorate throws while no handler exists.
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.Scan(s => s.FromAssemblyOf<ApplicationAssemblyMarker>()
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime());

        services.AddValidatorsFromAssemblyContaining<ApplicationAssemblyMarker>(includeInternalTypes: true);
        services.AddCommandHandlerDecorators();
        return services;
    }

    /// Applies the decorators to every command handler registered so far (also used by tests).
    public static IServiceCollection AddCommandHandlerDecorators(this IServiceCollection services)
    {
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(ValidatingCommandHandler<,>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(LoggingCommandHandler<,>));
        return services;
    }
}
