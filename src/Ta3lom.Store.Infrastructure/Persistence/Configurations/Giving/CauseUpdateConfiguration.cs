using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Giving;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Giving;

/// `cause_update` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class CauseUpdateConfiguration : IEntityTypeConfiguration<CauseUpdate>
{
    public void Configure(EntityTypeBuilder<CauseUpdate> e)
    {
        e.ToTable("cause_update", t =>
        {
            t.HasCheckConstraint("cause_update_units_delivered_check", "(units_delivered >= 0)");
        });
        e.HasKey(x => x.Id).HasName("cause_update_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.ProductId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.TitleAr).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.TitleEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.BodyAr).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.BodyEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.AssetId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.UnitsDelivered).HasColumnType("integer").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.OccurredOn).HasColumnType("date").IsRequired().ValueGeneratedNever();
        e.Property(x => x.IsPublic).HasColumnType("boolean").IsRequired().HasDefaultValueSql("true").ValueGeneratedNever();
        e.Property(x => x.NotifyDonors).HasColumnType("boolean").IsRequired().HasDefaultValueSql("false").ValueGeneratedNever();
        e.Property(x => x.NotifiedAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.CreatedBy).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.HasIndex(x => new { x.ProductId, x.OccurredOn }).IsDescending(false, true).HasDatabaseName("ix_cause_update_product");
        e.HasOne<Ta3lom.Store.Domain.Media.MediaAsset>().WithMany().HasForeignKey(x => x.AssetId).HasConstraintName("cause_update_asset_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Catalog.Product>().WithMany().HasForeignKey(x => x.ProductId).HasConstraintName("cause_update_product_id_fkey").OnDelete(DeleteBehavior.Cascade);
    }
}
