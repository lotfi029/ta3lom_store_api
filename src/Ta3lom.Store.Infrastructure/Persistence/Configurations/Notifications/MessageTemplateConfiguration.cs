using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Notifications;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Notifications;

/// `message_template` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> e)
    {
        e.ToTable("message_template", t =>
        {
            t.HasCheckConstraint("message_template_channel_check", "(channel = ANY (ARRAY['sms'::text, 'email'::text]))");
            t.HasCheckConstraint("message_template_locale_check", "(locale = ANY (ARRAY['ar'::bpchar, 'en'::bpchar]))");
        });
        e.HasKey(x => new { x.Key, x.Channel, x.Locale }).HasName("message_template_pkey");
        e.Property(x => x.Key).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Channel).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Locale).HasColumnType("character(2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Subject).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Body).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
    }
}
