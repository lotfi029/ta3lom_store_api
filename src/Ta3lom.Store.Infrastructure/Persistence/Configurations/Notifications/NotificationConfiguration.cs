using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Notifications;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Notifications;

/// `notification`: one row per send attempt; `dedupe_key` (unique) makes outbox sends at-most-once.
internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> e)
    {
        e.ToTable("notification", t =>
        {
            t.HasCheckConstraint("ck_notification_channel", "channel IN ('sms', 'email')");
            t.HasCheckConstraint("ck_notification_status", "status IN ('sent', 'failed')");
        });
        e.HasKey(x => x.Id).HasName("pk_notification");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Channel).HasColumnType("text").IsRequired();
        e.Property(x => x.RecipientHash).HasColumnType("character(64)").IsRequired();
        e.Property(x => x.TemplateKey).HasColumnType("text").IsRequired();
        e.Property(x => x.DedupeKey).HasColumnType("text");
        e.Property(x => x.Status).HasColumnType("text").IsRequired();
        e.Property(x => x.ProviderRef).HasColumnType("text");
        e.Property(x => x.CreatedAt).IsRequired();
        e.HasIndex(x => x.DedupeKey).IsUnique().HasDatabaseName("ux_notification_dedupe");
        e.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_notification_created");
    }
}
