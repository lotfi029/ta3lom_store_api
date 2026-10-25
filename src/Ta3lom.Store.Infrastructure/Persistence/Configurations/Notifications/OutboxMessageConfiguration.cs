using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Notifications;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Notifications;

/// `outbox_message`: claimed per row with a lease; `ix_outbox_pending` serves the claim query.
internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> e)
    {
        e.ToTable("outbox_message");
        e.HasKey(x => x.Id).HasName("pk_outbox_message");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Type).HasColumnType("text").IsRequired();
        e.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        e.Property(x => x.Attempts).HasDefaultValue(0).ValueGeneratedNever();
        e.Property(x => x.LastError).HasColumnType("text");
        e.Property(x => x.DeadLettered).HasDefaultValue(false).ValueGeneratedNever();
        e.HasIndex(x => x.NextAttemptAt).HasFilter("processed_at IS NULL AND NOT dead_lettered").HasDatabaseName("ix_outbox_pending");
        e.HasIndex(x => new { x.Type, x.OccurredAt }).HasDatabaseName("ix_outbox_type");
    }
}
