using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using Ta3lom.Store.Application.Abstractions;
using Ta3lom.Store.Infrastructure.Health;
using Ta3lom.Store.Infrastructure.Persistence;
using Ta3lom.Store.Infrastructure.Persistence.Interceptors;
using Ta3lom.Store.Infrastructure.Persistence.Seed;
using Ta3lom.Store.Infrastructure.Security;
using Ta3lom.Store.Infrastructure.Time;

namespace Ta3lom.Store.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Store";

    /// Adapters for the Application ports: clock, persistence (S-02), data protection, seed, health.
    /// SMS (S-04), email and documents (S-17) and the other adapters follow in their sessions.
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IClock, SystemClock>();

        AddPersistence(services);
        AddDataProtection(services);

        // Jobs (09 §6) are registered by the sessions that own them. RAM store, in-process.
        services.AddQuartz();
        services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
        return services;
    }

    private static void AddPersistence(IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>()
            .Configure<IConfiguration>((o, c) => o.ConnectionString = c.GetConnectionString(ConnectionStringName) ?? string.Empty)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), $"ConnectionStrings:{ConnectionStringName} is required")
            .ValidateOnStart();

        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<StoreDbContext>((sp, options) =>
        {
            StoreDbContextOptions.Configure(options, sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString);
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<StoreDbContext>());

        services.AddScoped<SeedRunner>();
        services.AddHostedService<SeedHostedService>();

        services.AddHealthChecks().AddCheck<PostgresHealthCheck>(PostgresHealthCheck.Name, tags: [HealthTags.Ready]);
    }

    /// Data Protection encrypts beneficiary PII (09 §5.13). Keys persist in `Store:DataProtection:KeysPath`
    /// (a volume in production; losing it makes PII unreadable). Certificate protection of the key ring is S-03.
    private static void AddDataProtection(IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName("ta3lom-store");
        services.AddOptions<KeyManagementOptions>().Configure<IConfiguration, ILoggerFactory>((o, c, loggers) =>
        {
            var path = c["Store:DataProtection:KeysPath"];
            if (!string.IsNullOrWhiteSpace(path))
            {
                o.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(path), loggers);
            }
        });

        services.AddOptions<PiiOptions>().BindConfiguration(PiiOptions.Section)
            .Validate(o => HashKeyLength(o.MobileHashKeyBase64) >= 32, "Store:Pii:MobileHashKeyBase64 must be at least 32 bytes (base64)")
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PiiOptions>, PiiOptionsProductionValidator>();
        services.AddSingleton<IPiiProtector, PiiProtector>();
    }

    private static int HashKeyLength(string base64)
    {
        try
        {
            return Convert.FromBase64String(base64).Length;
        }
        catch (FormatException)
        {
            return 0;
        }
    }
}

public sealed class DatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}

/// Production refuses the all-zero development key.
internal sealed class PiiOptionsProductionValidator(IHostEnvironment environment) : IValidateOptions<PiiOptions>
{
    public ValidateOptionsResult Validate(string? name, PiiOptions options) =>
        environment.IsProduction() && options.MobileHashKeyBase64 == Convert.ToBase64String(new byte[32])
            ? ValidateOptionsResult.Fail("The development PII hash key cannot be used in Production")
            : ValidateOptionsResult.Success;
}
