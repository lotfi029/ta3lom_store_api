using Microsoft.AspNetCore.Localization;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Ta3lom.Store.Api.Extensions;
using Ta3lom.Store.Api.Options;
using Ta3lom.Store.Api.Security;
using Ta3lom.Store.Application;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain;
using Ta3lom.Store.Infrastructure;

namespace Ta3lom.Store.Api;

public static class DependencyInjection
{
    /// The whole composition (DMS chaining): AddApi → AddInfrastructure → AddApplication → AddDomain.
    public static IServiceCollection AddStore(this IServiceCollection services, IConfiguration configuration) =>
        services
            .AddApi(configuration)
            .AddInfrastructure(configuration)
            .AddApplication()
            .AddDomain();

    /// Presentation services: options, endpoints, ProblemDetails, CORS, rate limits, localisation, OpenAPI, telemetry.
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        // Configuration is validated at start-up (09 §7.3).
        services.AddOptions<StoreOptions>()
            .BindConfiguration(StoreOptions.Section)
            .ValidateDataAnnotations()
            .Validate(o => o.Validate().Count == 0, "Store options are invalid; see StoreOptions.Validate()")
            .ValidateOnStart();

        services.AddEndpoints(typeof(DependencyInjection).Assembly);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentActor, HttpCurrentActor>();
        services.AddStoreAuthentication();

        services.AddStoreLogging();
        services.AddStoreProblemDetails();
        services.AddStoreForwardedHeaders();
        services.AddStoreCors();
        services.AddStoreRateLimiting();
        services.AddHsts(o =>
        {
            o.MaxAge = TimeSpan.FromDays(365);
            o.IncludeSubDomains = true;
        });
        services.AddRequestLocalization(o =>
        {
            string[] cultures = ["ar", "en"];
            o.SetDefaultCulture("ar").AddSupportedCultures(cultures).AddSupportedUICultures(cultures);
            o.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
        });
        services.AddHealthChecks();
        services.AddOpenApi();
        services.AddStoreTelemetry(configuration);
        return services;
    }

    private static void AddStoreTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var otlpEndpoint = configuration["Store:Telemetry:OtlpEndpoint"];
        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("ta3lom-store-api"))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddSource("Ta3lom.Store");
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation().AddMeter("Ta3lom.Store");
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    m.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            });
    }
}
