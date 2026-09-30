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
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.IRepositories;
using Ta3lom.Store.Infrastructure.Persistence;
using Ta3lom.Store.Infrastructure.Persistence.Interceptors;
using Ta3lom.Store.Infrastructure.Persistence.Repositories;
using Ta3lom.Store.Infrastructure.Persistence.Seeders;
using Ta3lom.Store.Infrastructure.Services.Health;
using Ta3lom.Store.Infrastructure.Services.Jobs;
using Ta3lom.Store.Infrastructure.Services.Security;
using Ta3lom.Store.Infrastructure.Services.Sms;
using Ta3lom.Store.Infrastructure.Services.Templates;
using Ta3lom.Store.Infrastructure.Services.Time;

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
        AddRepositories(services);
        AddSecurity(services);
        AddMessaging(services);

        // Jobs (09 §6): RAM store, in-process, Riyadh cron; each session adds its jobs here.
        services.AddQuartz(q =>
        {
            q.AddStoreJob<PruneOtpJob>(PruneOtpJob.Cron);
        });
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
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<StoreDbContext>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

        services.AddScoped<SeedRunner>();
        services.AddHostedService<SeedHostedService>();

        services.AddHealthChecks().AddCheck<PostgresHealthCheck>(PostgresHealthCheck.Name, tags: [HealthTags.Ready]);
    }

    private static void AddRepositories(IServiceCollection services)
    {
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IOtpChallengeRepository, OtpChallengeRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IStaffUserRepository, StaffUserRepository>();
    }

    private static void AddSecurity(IServiceCollection services)
    {
        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.Section).ValidateOnStart();
        services.AddOptions<OtpOptions>().BindConfiguration(OtpOptions.Section).ValidateOnStart();
        services.AddOptions<TokenKeyOptions>().BindConfiguration(TokenKeyOptions.Section).ValidateOnStart();
        services.AddOptions<DownloadKeyOptions>().BindConfiguration(DownloadKeyOptions.Section).ValidateOnStart();
        services.AddSingleton<KeyOptionsSetup>();
        services.AddSingleton<IPostConfigureOptions<JwtOptions>>(sp => sp.GetRequiredService<KeyOptionsSetup>());
        services.AddSingleton<IPostConfigureOptions<OtpOptions>>(sp => sp.GetRequiredService<KeyOptionsSetup>());
        services.AddSingleton<IPostConfigureOptions<TokenKeyOptions>>(sp => sp.GetRequiredService<KeyOptionsSetup>());
        services.AddSingleton<IPostConfigureOptions<DownloadKeyOptions>>(sp => sp.GetRequiredService<KeyOptionsSetup>());
        services.AddSingleton<IValidateOptions<JwtOptions>>(sp => sp.GetRequiredService<KeyOptionsSetup>());
        services.AddSingleton<IValidateOptions<OtpOptions>>(sp => sp.GetRequiredService<KeyOptionsSetup>());
        services.AddSingleton<IValidateOptions<TokenKeyOptions>>(sp => sp.GetRequiredService<KeyOptionsSetup>());
        services.AddSingleton<IValidateOptions<DownloadKeyOptions>>(sp => sp.GetRequiredService<KeyOptionsSetup>());

        services.AddSingleton<IOtpHasher, OtpHasher>();
        services.AddSingleton<IRefreshTokenCodec, RefreshTokenCodec>();
        services.AddSingleton<IPseudonymizer, Pseudonymizer>();
        services.AddSingleton<IAccessTokenIssuer, JwtTokenIssuer>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITotpService, TotpService>();
    }

    /// SMS (sink unless `Store:Sms:Provider = unifonic`) and templates.
    private static void AddMessaging(IServiceCollection services)
    {
        services.AddOptions<SmsOptions>().BindConfiguration(SmsOptions.Section);
        services.AddSingleton<SinkSmsSender>();
        services.AddSingleton<IDevSmsInbox>(sp => sp.GetRequiredService<SinkSmsSender>());
        services.AddHttpClient<UnifonicSmsSender>(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<ISmsSender>(sp =>
            string.Equals(sp.GetRequiredService<IOptions<SmsOptions>>().Value.Provider, "unifonic", StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<UnifonicSmsSender>()
                : sp.GetRequiredService<SinkSmsSender>());
        services.AddScoped<ITemplateRenderer, ScribanTemplateRenderer>();
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
