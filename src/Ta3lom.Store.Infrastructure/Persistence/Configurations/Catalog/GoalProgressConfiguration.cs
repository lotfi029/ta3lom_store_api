using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Catalog;

/// `goal_progress` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class GoalProgressConfiguration : IEntityTypeConfiguration<GoalProgress>
{
    public void Configure(EntityTypeBuilder<GoalProgress> e)
    {
        e.ToTable("goal_progress");
        e.HasKey(x => x.ProductId).HasName("goal_progress_pkey");
        e.Property(x => x.ProductId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.RaisedAmount).HasColumnType("numeric(14,2)").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.FundedUnits).HasColumnType("integer").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.DonorCount).HasColumnType("integer").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.ZakatAmount).HasColumnType("numeric(14,2)").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.SeedRaised).HasColumnType("numeric(14,2)").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.SeedUnits).HasColumnType("integer").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.SeedDonors).HasColumnType("integer").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.LastGiftAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        e.HasOne<Product>().WithOne().HasForeignKey<GoalProgress>(x => x.ProductId).HasConstraintName("goal_progress_product_id_fkey").OnDelete(DeleteBehavior.Cascade);
    }
}
