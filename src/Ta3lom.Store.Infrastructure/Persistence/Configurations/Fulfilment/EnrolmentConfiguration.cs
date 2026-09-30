using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Fulfilment;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Fulfilment;

/// `enrolment` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class EnrolmentConfiguration : IEntityTypeConfiguration<Enrolment>
{
    public void Configure(EntityTypeBuilder<Enrolment> e)
    {
        e.ToTable("enrolment", t =>
        {
            t.HasCheckConstraint("ck_enrol_source", "(((source = 'purchase'::text) AND (order_line_id IS NOT NULL)) OR ((source = 'sponsored'::text) AND (sponsored_unit_id IS NOT NULL)) OR (source = 'manual'::text))");
            t.HasCheckConstraint("enrolment_progress_pct_check", "((progress_pct >= 0) AND (progress_pct <= 100))");
            t.HasCheckConstraint("enrolment_source_check", "(source = ANY (ARRAY['purchase'::text, 'sponsored'::text, 'manual'::text]))");
            t.HasCheckConstraint("enrolment_status_check", "(status = ANY (ARRAY['pending_sync'::text, 'active'::text, 'completed'::text, 'revoked'::text, 'expired'::text]))");
        });
        e.HasKey(x => x.Id).HasName("enrolment_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.CustomerId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.ProductId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Source).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.OrderLineId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.SponsoredUnitId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Status).HasColumnType("text").IsRequired().HasDefaultValueSql("'active'::text").ValueGeneratedNever();
        e.Property(x => x.ProgressPct).HasColumnType("smallint").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.LmsEnrolmentRef).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.GrantedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.StartedAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CompletedAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CertificateNo).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.HasIndex(x => x.CertificateNo).IsUnique().HasDatabaseName("enrolment_certificate_no_key");
        e.HasIndex(x => new { x.CustomerId, x.ProductId }).IsUnique().HasFilter("(status = ANY (ARRAY['pending_sync'::text, 'active'::text, 'completed'::text]))").HasDatabaseName("ux_enrolment_live");
        e.HasOne<Ta3lom.Store.Domain.Identity.Customer>().WithMany().HasForeignKey(x => x.CustomerId).HasConstraintName("enrolment_customer_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Ordering.OrderLine>().WithMany().HasForeignKey(x => x.OrderLineId).HasConstraintName("enrolment_order_line_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Catalog.Product>().WithMany().HasForeignKey(x => x.ProductId).HasConstraintName("enrolment_product_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Giving.SponsoredUnit>().WithMany().HasForeignKey(x => x.SponsoredUnitId).HasConstraintName("fk_enrolment_unit").OnDelete(DeleteBehavior.NoAction);
    }
}
