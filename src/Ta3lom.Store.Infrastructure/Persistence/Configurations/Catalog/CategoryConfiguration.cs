using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Media;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Catalog;

/// `category` (BR-A01). `xmin` is the concurrency token.
internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> e)
    {
        e.ToTable("category", t => t.HasCheckConstraint("ck_category_slug", "slug ~ '^[a-z0-9-]{2,60}$'"));
        e.HasKey(x => x.Id).HasName("pk_category");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Slug).HasColumnType("text").IsRequired();
        e.Property(x => x.NameAr).HasColumnType("text").IsRequired();
        e.Property(x => x.NameEn).HasColumnType("text");
        e.Property(x => x.DescriptionAr).HasColumnType("text");
        e.Property(x => x.DescriptionEn).HasColumnType("text");
        e.Property(x => x.AcceptsGeneralGifts).HasDefaultValue(true).ValueGeneratedNever();
        e.Property(x => x.Sort).HasDefaultValue(0).ValueGeneratedNever();
        e.Property(x => x.IsActive).HasDefaultValue(true).ValueGeneratedNever();
        e.Property(x => x.Version).IsRowVersion();
        e.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("ux_category_slug");
        e.HasIndex(x => new { x.IsActive, x.Sort }).HasDatabaseName("ix_category_sort");
        e.HasIndex(x => x.ImageAssetId).HasDatabaseName("ix_category_image");
        e.HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.ImageAssetId).HasConstraintName("fk_category_image").OnDelete(DeleteBehavior.Restrict);
    }
}
