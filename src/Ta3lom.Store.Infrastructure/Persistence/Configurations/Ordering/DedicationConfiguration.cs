using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Ordering;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Ordering;

/// `dedication` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class DedicationConfiguration : IEntityTypeConfiguration<Dedication>
{
    public void Configure(EntityTypeBuilder<Dedication> e)
    {
        e.ToTable("dedication", t =>
        {
            t.HasCheckConstraint("dedication_custom_text_check", "(length(custom_text) <= 140)");
            t.HasCheckConstraint("dedication_sms_status_check", "(sms_status = ANY (ARRAY['pending'::text, 'sent'::text, 'failed'::text, 'skipped'::text]))");
            t.HasCheckConstraint("dedication_to_mobile_check", "(to_mobile ~ '^\\+9665[0-9]{8}$'::text)");
            t.HasCheckConstraint("dedication_to_name_check", "((length(to_name) >= 2) AND (length(to_name) <= 60))");
        });
        e.HasKey(x => x.Id).HasName("dedication_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.OrderLineId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.ToName).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.ToMobile).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.FromName).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.TemplateKey).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.CustomText).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CardToken).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.SmsStatus).HasColumnType("text").IsRequired().HasDefaultValueSql("'pending'::text").ValueGeneratedNever();
        e.Property(x => x.SentAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.HasIndex(x => x.CardToken).IsUnique().HasDatabaseName("dedication_card_token_key");
        e.HasIndex(x => x.OrderLineId).IsUnique().HasDatabaseName("dedication_order_line_id_key");
        e.HasOne<OrderLine>().WithMany().HasForeignKey(x => x.OrderLineId).HasConstraintName("dedication_order_line_id_fkey").OnDelete(DeleteBehavior.Cascade);
    }
}
