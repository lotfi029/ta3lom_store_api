using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Media;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Media;

/// `media_asset`: server-generated storage keys; images need Arabic alt text (`ck_media_alt`).
internal sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> e)
    {
        e.ToTable("media_asset", t =>
        {
            t.HasCheckConstraint("ck_media_alt", "kind <> 'image' OR alt_ar IS NOT NULL");
            t.HasCheckConstraint("ck_media_kind", "kind IN ('image', 'pdf')");
            t.HasCheckConstraint("ck_media_size", "size_bytes > 0");
        });
        e.HasKey(x => x.Id).HasName("pk_media_asset");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Kind).HasColumnType("text").IsRequired();
        e.Property(x => x.StorageKey).HasColumnType("text").IsRequired();
        e.Property(x => x.IsPublic).IsRequired();
        e.Property(x => x.Mime).HasColumnType("text").IsRequired();
        e.Property(x => x.SizeBytes).IsRequired();
        e.Property(x => x.AltAr).HasColumnType("text");
        e.Property(x => x.AltEn).HasColumnType("text");
        e.Property(x => x.CreatedAt).IsRequired();
        e.Property(x => x.CreatedBy).HasColumnType("text");
        e.HasIndex(x => x.StorageKey).IsUnique().HasDatabaseName("ux_media_storage_key");
        e.HasIndex(x => x.CreatedAt).IsDescending().HasDatabaseName("ix_media_created");
    }
}
