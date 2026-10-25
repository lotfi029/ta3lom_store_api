using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
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
using Ta3lom.Store.Infrastructure.Services.Email;
using Ta3lom.Store.Infrastructure.Services.Files;
using Ta3lom.Store.Infrastructure.Services.Health;
using Ta3lom.Store.Infrastructure.Services.Jobs;
using Ta3lom.Store.Infrastructure.Services.Operations;
using Ta3lom.Store.Infrastructure.Services.Outbox;
using Ta3lom.Store.Infrastructure.Services.Security;
using Ta3lom.Store.Infrastructure.Services.Settings;
using Ta3lom.Store.Infrastructure.Services.SiteRebuild;
using Ta3lom.Store.Infrastructure.Services.Sms;
using Ta3lom.Store.Infrastructure.Services.Templates;
using Ta3lom.Store.Infrastructure.Services.Time;

namespace Ta3lom.Store.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Store";

    /// Adapters for the Application ports: clock, persistence, data protection, security, messaging, files, jobs.
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
        AddFiles(services);

        // Jobs: RAM store, in-process, Riyadh cron. Each is a thin shell around one Application command.
        services.AddQuartz(q =>
        {
            q.AddStoreJob<PruneOtpJob>(PruneOtpJob.Cron);
            q.AddStoreJob<PruneIdempotencyKeysJob>(PruneIdempotencyKeysJob.Cron);
            q.AddStoreJob<ExpireGuestCartsJob>(ExpireGuestCartsJob.Cron);
            q.AddStoreJob<RetentionJob>(RetentionJob.Cron);
            q.AddStoreJob<IntegrityChecksJob>(IntegrityChecksJob.Cron);
        });
        services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
        services.AddSingleton<IJobRunner, QuartzJobRunner>();
        return services;
    }

    private static void AddPersistence(IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>()
            .Configure<IConfiguration>((o, c) => o.ConnectionString = c.GetConnectionString(ConnectionStringName) ?? string.Empty)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), $"ConnectionStrings:{ConnectionStringName} is required")
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseSearchPathValidator>();

        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddScoped<OutboxSaveChangesInterceptor>();
        services.AddScoped<IOutbox, Services.Outbox.Outbox>();
        services.AddDbContext<StoreDbContext>((sp, options) =>
        {
            StoreDbContextOptions.Configure(options, sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString);
            // Audit first (it audits business rows), then the outbox rows for the raised domain events.
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>(), sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<StoreDbContext>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

        services.AddScoped<SeedRunner>();
        services.AddScoped<DevelopmentCatalogSeeder>();
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
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<ICatalogSearch, Services.Catalog.CatalogSearch>();
        services.AddScoped<IOperationsStatus, OperationsStatus>();
        services.AddScoped<IOperationsMaintenance, OperationsMaintenance>();
    }

    private static void AddSecurity(IServiceCollection services)
    {
        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.Section).ValidateOnStart();
        services.AddOptions<OtpOptions>().BindConfiguration(OtpOptions.Section).ValidateOnStart();
        services.AddOptions<TokenKeyOptions>().BindConfiguration(TokenKeyOptions.Section).ValidateOnStart();
        services.AddOptions<HashingOptions>().BindConfiguration(HashingOptions.Section).ValidateOnStart();
        services.AddSingleton<KeyOptionsSetup>();
        services.AddSingleton<IPostConfigureOptions<JwtOptions>>(sp => sp.GetRequiredService<KeyOptionsSetup>());
        services.AddSingleton<IPostConfigureOptions<OtpOptions>>(sp => sp.GetRequiredService<KeyOptionsSetup>());
        services.AddSingleton<IPostConfigureOptions<TokenKeyOptions>>(sp => sp.GetRequiredService<KeyOptionsSetup>());
        services.AddSingleton<IPostConfigureOptions<HashingOptions>>(sp => sp.GetRequiredService<KeyOptionsSetup>());
        services.AddSingleton<IValidateOptions<JwtOptions>>(sp => sp.GetRequiredService<KeyOptionsSetup>());
        services.AddSingleton<IValidateOptions<OtpOptions>>(sp => sp.GetRequiredService<KeyOptionsSetup>());
        services.AddSingleton<IValidateOptions<TokenKeyOptions>>(sp => sp.GetRequiredService<KeyOptionsSetup>());
        services.AddSingleton<IValidateOptions<HashingOptions>>(sp => sp.GetRequiredService<KeyOptionsSetup>());

        services.AddSingleton<IOtpHasher, OtpHasher>();
        services.AddSingleton<IRefreshTokenCodec, RefreshTokenCodec>();
        services.AddSingleton<IPseudonymizer, Pseudonymizer>();
        services.AddScoped<IIdempotencyStore, Services.Idempotency.IdempotencyStore>();
        services.AddSingleton<IAccessTokenIssuer, JwtTokenIssuer>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddMemoryCache();
        services.AddSingleton<IUsedTokenStore, MemoryUsedTokenStore>();
        services.AddSingleton<IOrderStatusTokens, OrderStatusTokens>();
        services.AddScoped<IAuditTrail, Services.Auditing.AuditTrail>();
    }

    /// SMS (sink unless `Store:Sms:Provider = unifonic`), e-mail (sink unless `Store:Email:Mode = smtp`), templates, outbox, settings.
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

        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.Section);
        services.AddSingleton<SinkEmailSender>();
        services.AddSingleton<IDevMailInbox>(sp => sp.GetRequiredService<SinkEmailSender>());
        services.AddSingleton<SmtpEmailSender>();
        services.AddSingleton<IEmailSender>(sp =>
            string.Equals(sp.GetRequiredService<IOptions<EmailOptions>>().Value.Mode, "smtp", StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<SmtpEmailSender>()
                : sp.GetRequiredService<SinkEmailSender>());
        services.AddOptions<OutboxOptions>().BindConfiguration(OutboxOptions.Section);
        services.AddSingleton<OutboxSignal>();
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());

        services.AddOptions<SiteRebuildOptions>().BindConfiguration(SiteRebuildOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<SiteRebuildOptions>, SiteRebuildOptionsValidator>();
        services.AddHttpClient<ISiteRebuildTrigger, GitHubSiteRebuildTrigger>(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<ICatalogChangeDebounce, CatalogChangeDebounce>();

        services.AddSingleton<StoreSettingsProvider>();
        services.AddSingleton<IStoreSettings>(sp => sp.GetRequiredService<StoreSettingsProvider>());
    }

    private static void AddFiles(IServiceCollection services)
    {
        services.AddOptions<FileStorageOptions>().BindConfiguration(FileStorageOptions.Section);
        services.AddSingleton<IFileUrls, FileUrls>();
        services.AddSingleton<LocalFileStorage>();
        services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<LocalFileStorage>());
        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();
    }

    /// Data Protection seals TOTP secrets and the guest cart cookie. Keys persist in `Store:DataProtection:KeysPath`
    /// (a volume in production; losing it invalidates enrolled authenticators and guest carts).
    private static void AddDataProtection(IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName("ta3lom-store");
        services.AddOptions<DataProtectionKeysOptions>().BindConfiguration(DataProtectionKeysOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<DataProtectionKeysOptions>, DataProtectionKeysValidator>();
        services.AddOptions<KeyManagementOptions>().Configure<IConfiguration, ILoggerFactory, IHostEnvironment>((o, c, loggers, environment) =>
        {
            var path = c["Store:DataProtection:KeysPath"];
            if (!string.IsNullOrWhiteSpace(path))
            {
                // A relative path (Development) resolves against the content root, never the working directory.
                o.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(Path.GetFullPath(Path.Combine(environment.ContentRootPath, path))), loggers);
            }
        });
    }
}

public sealed class DatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}

/// `Store:DataProtection` — where the key ring lives (TOTP secrets, guest cart cookies).
public sealed class DataProtectionKeysOptions
{
    public const string Section = "Store:DataProtection";

    public string KeysPath { get; set; } = string.Empty;
}

/// L-9: losing the key ring makes every enrolled authenticator unreadable, so outside Development the host refuses to
/// start when `Store:DataProtection:KeysPath` is empty or not writable (a probe file is written and deleted).
internal sealed class DataProtectionKeysValidator(IHostEnvironment environment) : IValidateOptions<DataProtectionKeysOptions>
{
    public ValidateOptionsResult Validate(string? name, DataProtectionKeysOptions options)
    {
        if (environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.KeysPath))
        {
            return ValidateOptionsResult.Fail("Store:DataProtection:KeysPath is required outside Development (the key ring must persist)");
        }

        var directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.KeysPath));
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-probe-{Guid.CreateVersion7():N}");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return ValidateOptionsResult.Success;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ValidateOptionsResult.Fail($"Store:DataProtection:KeysPath '{directory}' is not writable: {ex.Message}");
        }
    }
}

/// A-4: the extensions (citext, pg_trgm) live in `public`. A `Search Path` that leaves `public` out breaks citext
/// comparisons (staff e-mail) and the trigram search: refused outside Development, a warning in Development. No
/// `Search Path` at all is fine (the server default includes `public`; the tables are schema-qualified).
internal sealed partial class DatabaseSearchPathValidator(IHostEnvironment environment, ILogger<DatabaseSearchPathValidator> logger)
    : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        string? searchPath;
        try
        {
            searchPath = new Npgsql.NpgsqlConnectionStringBuilder(options.ConnectionString).SearchPath;
        }
        catch (ArgumentException)
        {
            return ValidateOptionsResult.Fail("ConnectionStrings:Store is not a valid PostgreSQL connection string");
        }

        if (string.IsNullOrWhiteSpace(searchPath)
            || searchPath.Split(',', StringSplitOptions.TrimEntries).Any(s => string.Equals(s.Trim('"'), "public", StringComparison.OrdinalIgnoreCase)))
        {
            return ValidateOptionsResult.Success;
        }

        if (environment.IsDevelopment())
        {
            LogSearchPath(logger, searchPath);
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail($"ConnectionStrings:Store Search Path '{searchPath}' must include public (citext, pg_trgm), e.g. Search Path=store,public");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "ConnectionStrings:Store Search Path {SearchPath} does not include public (citext, pg_trgm live there)")]
    private static partial void LogSearchPath(ILogger logger, string searchPath);
}
