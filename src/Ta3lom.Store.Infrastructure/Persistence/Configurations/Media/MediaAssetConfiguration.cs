using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Media;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Media;

/// `media_asset` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> e)
    {
        e.ToTable("media_asset", t =>
        {
            t.HasCheckConstraint("ck_media_alt", "((kind <> 'image'::text) OR (alt_ar IS NOT NULL))");
            t.HasCheckConstraint("ck_media_private_book", "((kind <> 'book_file'::text) OR (is_public = false))");
            t.HasCheckConstraint("media_asset_kind_check", "(kind = ANY (ARRAY['image'::text, 'pdf'::text, 'book_file'::text]))");
            t.HasCheckConstraint("media_asset_size_bytes_check", "(size_bytes > 0)");
        });
        e.HasKey(x => x.Id).HasName("media_asset_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.Kind).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.StorageKey).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.IsPublic).HasColumnType("boolean").IsRequired().HasDefaultValueSql("true").ValueGeneratedNever();
        e.Property(x => x.Mime).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.SizeBytes).HasColumnType("bigint").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Width).HasColumnType("integer").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Height).HasColumnType("integer").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.AltAr).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.AltEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.HasIndex(x => x.StorageKey).IsUnique().HasDatabaseName("media_asset_storage_key_key");
    }
}
