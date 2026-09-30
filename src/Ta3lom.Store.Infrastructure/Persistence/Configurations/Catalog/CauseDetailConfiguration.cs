using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Catalog;

/// `cause_detail` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
// Expression indexes created in the migration with raw SQL: ux_one_general_fund
internal sealed class CauseDetailConfiguration : IEntityTypeConfiguration<CauseDetail>
{
    public void Configure(EntityTypeBuilder<CauseDetail> e)
    {
        e.ToTable("cause_detail", t =>
        {
            t.HasCheckConstraint("cause_detail_min_amount_check", "(min_amount > (0)::numeric)");
            t.HasCheckConstraint("cause_detail_target_amount_check", "(target_amount > (0)::numeric)");
            t.HasCheckConstraint("cause_detail_unit_amount_check", "((unit_amount IS NULL) OR (unit_amount > (0)::numeric))");
            t.HasCheckConstraint("ck_cause_label", "((unit_amount IS NULL) OR (unit_label_ar IS NOT NULL))");
            t.HasCheckConstraint("ck_cause_mode", "((unit_amount IS NOT NULL) OR allow_custom_amount)");
        });
        e.HasKey(x => x.ProductId).HasName("cause_detail_pkey");
        e.Property(x => x.ProductId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.UnitAmount).HasColumnType("numeric(12,2)").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.UnitLabelAr).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.UnitLabelEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.TargetAmount).HasColumnType("numeric(14,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.AllowCustomAmount).HasColumnType("boolean").IsRequired().HasDefaultValueSql("true").ValueGeneratedNever();
        e.Property(x => x.MinAmount).HasColumnType("numeric(12,2)").IsRequired().HasDefaultValueSql("10").ValueGeneratedNever();
        e.Property(x => x.ZakatEligible).HasColumnType("boolean").IsRequired().HasDefaultValueSql("false").ValueGeneratedNever();
        e.Property(x => x.BeneficiariesAr).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.BeneficiariesEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CloseOnTarget).HasColumnType("boolean").IsRequired().HasDefaultValueSql("false").ValueGeneratedNever();
        e.Property(x => x.IsGeneralFund).HasColumnType("boolean").IsRequired().HasDefaultValueSql("false").ValueGeneratedNever();
        e.Property(x => x.StartsAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.EndsAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.HasOne<Product>().WithOne().HasForeignKey<CauseDetail>(x => x.ProductId).HasConstraintName("cause_detail_product_id_fkey").OnDelete(DeleteBehavior.Cascade);
    }
}
