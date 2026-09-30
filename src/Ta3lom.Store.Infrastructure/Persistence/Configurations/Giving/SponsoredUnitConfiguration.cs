using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Giving;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Giving;

/// `sponsored_unit` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class SponsoredUnitConfiguration : IEntityTypeConfiguration<SponsoredUnit>
{
    public void Configure(EntityTypeBuilder<SponsoredUnit> e)
    {
        e.ToTable("sponsored_unit", t =>
        {
            t.HasCheckConstraint("ck_unit_alloc", "((status <> ALL (ARRAY['allocated'::text, 'completed'::text])) OR (beneficiary_id IS NOT NULL))");
            t.HasCheckConstraint("ck_unit_source", "((source = 'full'::text) = (order_line_id IS NOT NULL))");
            t.HasCheckConstraint("sponsored_unit_source_check", "(source = ANY (ARRAY['full'::text, 'pooled'::text]))");
            t.HasCheckConstraint("sponsored_unit_status_check", "(status = ANY (ARRAY['awaiting_allocation'::text, 'allocated'::text, 'completed'::text, 'lapsed'::text, 'cancelled'::text]))");
        });
        e.HasKey(x => x.Id).HasName("sponsored_unit_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.ProductId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.SponsorshipOfferId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Source).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.OrderLineId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.UnitPrice).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.HasZakat).HasColumnType("boolean").IsRequired().HasDefaultValueSql("false").ValueGeneratedNever();
        e.Property(x => x.Status).HasColumnType("text").IsRequired().HasDefaultValueSql("'awaiting_allocation'::text").ValueGeneratedNever();
        e.Property(x => x.BeneficiaryId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.AllocationBatchId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.FundedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.AllocatedAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.AllocatedBy).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CompletedAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.LapsedAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        e.HasIndex(x => x.BeneficiaryId).HasDatabaseName("ix_unit_beneficiary");
        e.HasIndex(x => new { x.ProductId, x.FundedAt }).HasFilter("(status = 'awaiting_allocation'::text)").HasDatabaseName("ix_unit_fifo");
        e.HasIndex(x => x.OrderLineId).HasDatabaseName("ix_unit_orderline");
        e.HasOne<AllocationBatch>().WithMany().HasForeignKey(x => x.AllocationBatchId).HasConstraintName("sponsored_unit_allocation_batch_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Beneficiary>().WithMany().HasForeignKey(x => x.BeneficiaryId).HasConstraintName("sponsored_unit_beneficiary_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Entities.Ordering.OrderLine>().WithMany().HasForeignKey(x => x.OrderLineId).HasConstraintName("sponsored_unit_order_line_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Entities.Catalog.Product>().WithMany().HasForeignKey(x => x.ProductId).HasConstraintName("sponsored_unit_product_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Entities.Catalog.SponsorshipOffer>().WithMany().HasForeignKey(x => x.SponsorshipOfferId).HasConstraintName("sponsored_unit_sponsorship_offer_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
