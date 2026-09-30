using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Notifications;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Notifications;

/// `notification` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> e)
    {
        e.ToTable("notification", t =>
        {
            t.HasCheckConstraint("notification_channel_check", "(channel = ANY (ARRAY['sms'::text, 'email'::text]))");
            t.HasCheckConstraint("notification_status_check", "(status = ANY (ARRAY['queued'::text, 'sent'::text, 'failed'::text]))");
        });
        e.HasKey(x => x.Id).HasName("notification_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.Channel).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Recipient).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.TemplateKey).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Locale).HasColumnType("character(2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Status).HasColumnType("text").IsRequired().HasDefaultValueSql("'queued'::text").ValueGeneratedNever();
        e.Property(x => x.ProviderMsgId).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Error).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.RelatedOrderId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.OutboxMessageId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.SentAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.HasIndex(x => x.RelatedOrderId).HasDatabaseName("ix_notification_order");
        e.HasIndex(x => new { x.OutboxMessageId, x.Channel, x.Recipient }).IsUnique().HasFilter("(outbox_message_id IS NOT NULL)").HasDatabaseName("ux_notification_once");
        e.HasOne<OutboxMessage>().WithMany().HasForeignKey(x => x.OutboxMessageId).HasConstraintName("notification_outbox_message_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Entities.Ordering.Order>().WithMany().HasForeignKey(x => x.RelatedOrderId).HasConstraintName("notification_related_order_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
