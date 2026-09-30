using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Notifications;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Notifications;

/// `outbox_message` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> e)
    {
        e.ToTable("outbox_message");
        e.HasKey(x => x.Id).HasName("outbox_message_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.Type).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Payload).HasColumnType("jsonb").IsRequired().ValueGeneratedNever();
        e.Property(x => x.OccurredAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.ProcessedAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Attempts).HasColumnType("integer").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.NextAttemptAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.LastError).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.HasIndex(x => x.NextAttemptAt).HasFilter("(processed_at IS NULL)").HasDatabaseName("ix_outbox_pending");
    }
}
