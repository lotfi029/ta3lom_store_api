using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Domain.Entities.Auditing;
using Ta3lom.Store.Domain.Entities.Carts;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Documents;
using Ta3lom.Store.Domain.Entities.Fulfilment;
using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Domain.Entities.Media;
using Ta3lom.Store.Domain.Entities.Notifications;
using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Domain.Entities.Payments;
using Ta3lom.Store.Domain.Entities.Settings;
using Ta3lom.Store.Domain.ReadModels;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.Infrastructure.Persistence.Converters;
using Ta3lom.Store.Infrastructure.Services.Security;

namespace Ta3lom.Store.Infrastructure.Persistence;

/// The store's DbContext. The model mirrors db/reference/001_store_schema.sql exactly;
/// SchemaParityTests fails on any difference (tables, columns, defaults, CHECKs, indexes, FKs).
/// Commands use it through repositories + <see cref="Repositories.UnitOfWork"/>; queries through
/// <see cref="IApplicationDbContext"/> (DMS, K-c).
public sealed class StoreDbContext(DbContextOptions<StoreDbContext> options, IPiiProtector pii)
    : DbContext(options), IApplicationDbContext
{
    public const string Schema = "store";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public DbSet<Cart> Carts => Set<Cart>();

    public DbSet<CartLine> CartLines => Set<CartLine>();

    public DbSet<BookDetail> BookDetails => Set<BookDetail>();

    public DbSet<BookFormat> BookFormats => Set<BookFormat>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<CauseDetail> CauseDetails => Set<CauseDetail>();

    public DbSet<CourseDetail> CourseDetails => Set<CourseDetail>();

    public DbSet<CourseModule> CourseModules => Set<CourseModule>();

    public DbSet<GoalAdjustment> GoalAdjustments => Set<GoalAdjustment>();

    public DbSet<GoalProgress> GoalProgress => Set<GoalProgress>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<SponsorshipOffer> SponsorshipOffers => Set<SponsorshipOffer>();

    public DbSet<DocumentCounter> DocumentCounters => Set<DocumentCounter>();

    public DbSet<DonationReceipt> DonationReceipts => Set<DonationReceipt>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<DownloadGrant> DownloadGrants => Set<DownloadGrant>();

    public DbSet<DownloadLogEntry> DownloadLog => Set<DownloadLogEntry>();

    public DbSet<Enrolment> Enrolments => Set<Enrolment>();

    public DbSet<Shipment> Shipments => Set<Shipment>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<AllocationBatch> AllocationBatches => Set<AllocationBatch>();

    public DbSet<Beneficiary> Beneficiaries => Set<Beneficiary>();

    public DbSet<CauseUpdate> CauseUpdates => Set<CauseUpdate>();

    public DbSet<Distribution> Distributions => Set<Distribution>();

    public DbSet<PoolContribution> PoolContributions => Set<PoolContribution>();

    public DbSet<SponsoredUnit> SponsoredUnits => Set<SponsoredUnit>();

    public DbSet<SponsoredUnitEvent> SponsoredUnitEvents => Set<SponsoredUnitEvent>();

    public DbSet<SponsorshipPool> SponsorshipPools => Set<SponsorshipPool>();

    public DbSet<UnitFunding> UnitFundings => Set<UnitFunding>();

    public DbSet<ZakatLedgerEntry> ZakatLedger => Set<ZakatLedgerEntry>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();

    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<StaffUser> StaffUsers => Set<StaffUser>();

    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<Dedication> Dedications => Set<Dedication>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();

    public DbSet<Refund> Refunds => Set<Refund>();

    public DbSet<RefundLine> RefundLines => Set<RefundLine>();

    public DbSet<Setting> Settings => Set<Setting>();

    public DbSet<GoalView> Goals => Set<GoalView>();

    public DbSet<SalesDailyView> SalesDaily => Set<SalesDailyView>();

    public DbSet<AllocationBacklogView> AllocationBacklog => Set<AllocationBacklogView>();

    public DbSet<ZakatBalanceView> ZakatBalances => Set<ZakatBalanceView>();

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
        modelBuilder.Entity<AllocationBacklogView>().Property(v => v.Overdue30).HasColumnName("overdue_30");   // the convention would say overdue30
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
