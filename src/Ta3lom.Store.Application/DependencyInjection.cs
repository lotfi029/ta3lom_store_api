using FluentValidation;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Application.Abstractions.Behaviors;

namespace Ta3lom.Store.Application;

public static class DependencyInjection
{
    /// Registers every handler, validator and Mapster config of this assembly (DMS `AddApplication`),
    /// then wraps the handlers: Logging → Validation → handler.
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.Scan(s => s.FromAssemblyOf<ApplicationAssemblyMarker>()
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo<Features.Settlement.ILineSettlementEffect>(), publicOnly: false)   // one per line case (§13.4)
                .As<Features.Settlement.ILineSettlementEffect>().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo<Features.Outbox.IOutboxHandler>(), publicOnly: false)               // one per message type
                .As<Features.Outbox.IOutboxHandler>().WithScopedLifetime());

        services.AddValidatorsFromAssemblyContaining<ApplicationAssemblyMarker>(includeInternalTypes: true);
        services.AddHandlerDecorators();
        services.AddMapping();

        services.AddScoped<ICartMergeHook, Features.Cart.CartMergeHook>();            // RUL-17 (S-13 fills the S-04 hook)
        services.AddScoped<Features.Cart.CartCatalog>();
        services.AddScoped<Features.Cart.CartAccess>();
        services.AddScoped<Features.Checkout.CheckoutPricing>();
        services.AddScoped<Features.Settlement.SettlementLedger>();
        services.AddScoped<Features.Orders.OrderReader>();
        services.AddScoped<Features.Orders.OrderStock>();
        services.AddScoped<Features.Notifications.NotificationDispatch>();
        services.AddScoped<Features.Outbox.Handlers.OrderMessages>();
        services.AddScoped<Features.Documents.OrderDocuments>();
        services.AddScoped<Features.Fulfilment.FulfilmentReader>();
        services.AddScoped<Features.Settlement.IOrderSettlement, Features.Settlement.OrderSettlement>();
        services.AddScoped<Features.Catalog.CatalogReader>();
        services.AddScoped<Features.AdminCatalog.AdminCatalogReader>();
        return services;
    }

    /// Applies the decorators to every handler registered so far (also used by tests).
    /// TryDecorate is used because Scrutor's Decorate throws while no handler of a shape exists.
    public static IServiceCollection AddHandlerDecorators(this IServiceCollection services)
    {
        services.TryDecorate(typeof(ICommandHandler<>), typeof(ValidationDecorator.CommandHandler<>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator.CommandHandler<,>));
        services.TryDecorate(typeof(IQueryHandler<,>), typeof(ValidationDecorator.QueryHandler<,>));

        services.TryDecorate(typeof(ICommandHandler<>), typeof(LoggingDecorator.CommandHandler<>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(LoggingDecorator.CommandHandler<,>));
        services.TryDecorate(typeof(IQueryHandler<,>), typeof(LoggingDecorator.QueryHandler<,>));
        return services;
    }

    /// One TypeAdapterConfig for the app, filled from every IRegister in this assembly (explicit maps, K-h).
    public static IServiceCollection AddMapping(this IServiceCollection services)
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(ApplicationAssemblyMarker).Assembly);
        services.AddSingleton(config);
        services.AddScoped<IMapper, ServiceMapper>();
        return services;
    }
}
