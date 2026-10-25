using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Domain.Entities.Auditing;
using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Domain.Entities.Media;
using Ta3lom.Store.Domain.Entities.Notifications;
using Ta3lom.Store.Domain.Entities.Settings;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.Infrastructure.Persistence.Converters;

namespace Ta3lom.Store.Infrastructure.Persistence;

/// The store's DbContext. The EF model plus the `InitialSchema` migration are the source of truth of the schema.
/// Commands use it through repositories + <see cref="Repositories.UnitOfWork"/>; queries through <see cref="IApplicationDbContext"/>.
public sealed class StoreDbContext(DbContextOptions<StoreDbContext> options) : DbContext(options), IApplicationDbContext
{
    public const string Schema = "store";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<StaffUser> StaffUsers => Set<StaffUser>();

    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreDbContext).Assembly);
        ApplyEnumConverters(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Money>().HaveConversion<MoneyConverter>().HavePrecision(12, 2);
        configurationBuilder.Properties<decimal>().HavePrecision(12, 2);

        // Every index in the schema is declared explicitly; EF must not add one per foreign key.
        configurationBuilder.Conventions.Remove<ForeignKeyIndexConvention>();
    }

    /// Enums are stored as their snake_case text (`pending_payment`) behind a CHECK list.
    private static void ApplyEnumConverters(ModelBuilder modelBuilder)
    {
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
        {
            var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (enumType.IsEnum)
            {
                var converter = (ValueConverter)Activator.CreateInstance(typeof(SnakeCaseEnumConverter<>).MakeGenericType(enumType))!;
                property.SetValueConverter(converter);
            }
        }
    }
}
