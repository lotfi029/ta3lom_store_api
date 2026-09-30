using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Giving;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Giving;

/// `distribution` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class DistributionConfiguration : IEntityTypeConfiguration<Distribution>
{
    public void Configure(EntityTypeBuilder<Distribution> e)
    {
        e.ToTable("distribution");
        e.HasKey(x => x.Id).HasName("distribution_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.SponsoredUnitId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.BookFormatId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.DeliveredOn).HasColumnType("date").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.ProofAssetId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.HasIndex(x => x.SponsoredUnitId).IsUnique().HasDatabaseName("distribution_sponsored_unit_id_key");
        e.HasOne<Ta3lom.Store.Domain.Entities.Catalog.BookFormat>().WithMany().HasForeignKey(x => x.BookFormatId).HasConstraintName("distribution_book_format_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Entities.Media.MediaAsset>().WithMany().HasForeignKey(x => x.ProofAssetId).HasConstraintName("distribution_proof_asset_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<SponsoredUnit>().WithMany().HasForeignKey(x => x.SponsoredUnitId).HasConstraintName("distribution_sponsored_unit_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
