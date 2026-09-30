using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Payments;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Payments;

/// `payment_event` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> e)
    {
        e.ToTable("payment_event");
        e.HasKey(x => x.Id).HasName("payment_event_pkey");
        e.Property(x => x.Id).HasColumnType("bigint").IsRequired().UseIdentityAlwaysColumn();
        e.Property(x => x.Provider).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.ProviderEventId).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.EventType).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.SignatureValid).HasColumnType("boolean").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Payload).HasColumnType("jsonb").IsRequired().ValueGeneratedNever();
        e.Property(x => x.ReceivedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.ProcessedAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Error).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.HasIndex(x => new { x.Provider, x.ProviderEventId }).IsUnique().HasDatabaseName("payment_event_provider_provider_event_id_key");
    }
}
