using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ta3lom.Store.Application.Abstractions;
using Ta3lom.Store.Domain.Common;
using Ta3lom.Store.Domain.Giving;
using Ta3lom.Store.Infrastructure.Persistence.Converters;
using Ta3lom.Store.Infrastructure.Persistence.ReadModels;
using Ta3lom.Store.Infrastructure.Security;

namespace Ta3lom.Store.Infrastructure.Persistence;

/// The unit of work (09 §3.4). The model mirrors db/reference/001_store_schema.sql exactly;
/// SchemaParityTests fails on any difference (tables, columns, defaults, CHECKs, indexes, FKs).
public sealed class StoreDbContext(DbContextOptions<StoreDbContext> options, IPiiProtector pii)
    : DbContext(options), IUnitOfWork
{
    public const string Schema = "store";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasPostgresExtension(Schema, "citext");
        modelBuilder.HasPostgresExtension(Schema, "pg_trgm");
        modelBuilder.HasSequence<long>("order_ref_seq").StartsAt(1000);        // RUL-28
        modelBuilder.HasSequence<long>("beneficiary_code_seq").StartsAt(1);    // B-000123

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreDbContext).Assembly);

        // Beneficiary PII (PDPL): string in the domain, Data Protection ciphertext in bytea.
        var protectedString = new ProtectedStringConverter(pii);
        modelBuilder.Entity<Beneficiary>(e =>
        {
            e.Property(x => x.FullName).HasConversion(protectedString);
            e.Property(x => x.Mobile).HasConversion(protectedString!);
            e.Property(x => x.GuardianName).HasConversion(protectedString!);
        });

        // Read models over the reporting views (created by raw SQL in the migration).
        MapView<GoalView>(modelBuilder, "v_goal");
        MapView<SalesDailyView>(modelBuilder, "v_sales_daily");
        MapView<AllocationBacklogView>(modelBuilder, "v_allocation_backlog");
        MapView<ZakatBalanceView>(modelBuilder, "v_zakat_balance");

        ApplyEnumConverters(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Money>().HaveConversion<MoneyConverter>().HavePrecision(12, 2);
        configurationBuilder.Properties<decimal>().HavePrecision(12, 2);

        // Every index in the schema is declared explicitly; EF must not add one per foreign key.
        configurationBuilder.Conventions.Remove<ForeignKeyIndexConvention>();
    }

    /// Keyless read model over a view. The table mapping is cleared so the default schema does not
    /// leave a nameless table in the migration snapshot.
    private static void MapView<TView>(ModelBuilder modelBuilder, string view)
        where TView : class
    {
        var entity = modelBuilder.Entity<TView>().HasNoKey();
        entity.ToTable((string?)null);
        entity.ToView(view, Schema);
        entity.Metadata.SetSchema(null);
    }

    /// Enums are stored as the DDL's snake_case text (database/01 §2).
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
