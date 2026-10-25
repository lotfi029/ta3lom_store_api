using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Notifications;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Notifications;

/// `message_template`: key + channel + locale.
internal sealed class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> e)
    {
        e.ToTable("message_template", t =>
        {
            t.HasCheckConstraint("ck_template_channel", "channel IN ('sms', 'email')");
            t.HasCheckConstraint("ck_template_locale", "locale IN ('ar', 'en')");
        });
        e.HasKey(x => new { x.Key, x.Channel, x.Locale }).HasName("pk_message_template");
        e.Property(x => x.Key).HasColumnType("text");
        e.Property(x => x.Channel).HasColumnType("text");
        e.Property(x => x.Locale).HasColumnType("character(2)");
        e.Property(x => x.Subject).HasColumnType("text");
        e.Property(x => x.Body).HasColumnType("text").IsRequired();
        e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.UpdatedBy).HasColumnType("text");
    }
}
