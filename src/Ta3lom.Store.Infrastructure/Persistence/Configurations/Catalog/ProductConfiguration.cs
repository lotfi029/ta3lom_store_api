using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Catalog;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Catalog;

/// `product` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
// Expression indexes created in the migration with raw SQL: ix_product_trgm
internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> e)
    {
        e.ToTable("product", t =>
        {
            t.HasCheckConstraint("product_badge_check", "(badge = ANY (ARRAY['best'::text, 'new'::text, 'urgent'::text]))");
            t.HasCheckConstraint("product_slug_check", "(slug ~ '^[a-z0-9-]{2,80}$'::text)");
            t.HasCheckConstraint("product_status_check", "(status = ANY (ARRAY['draft'::text, 'published'::text, 'archived'::text]))");
            t.HasCheckConstraint("product_title_ar_check", "((length(title_ar) >= 2) AND (length(title_ar) <= 160))");
            t.HasCheckConstraint("product_type_check", "(type = ANY (ARRAY['course'::text, 'book'::text, 'cause'::text]))");
        });
        e.HasKey(x => x.Id).HasName("product_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.Type).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Slug).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Status).HasColumnType("text").IsRequired().HasDefaultValueSql("'draft'::text").ValueGeneratedNever();
        e.Property(x => x.CategoryId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CoverAssetId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.TitleAr).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.TitleEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.SummaryAr).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.SummaryEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.BodyAr).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.BodyEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Badge).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Sort).HasColumnType("integer").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.SeoTitleAr).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.SeoTitleEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.SeoDescAr).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.SeoDescEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.PublishedAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property<NpgsqlTypes.NpgsqlTsVector>("SearchVector").HasColumnName("search_vector").HasColumnType("tsvector")
            .HasComputedColumnSql("to_tsvector('simple'::regconfig, ar_normalize(((((((COALESCE(title_ar, ''::text) || ' '::text) || COALESCE(title_en, ''::text)) || ' '::text) || COALESCE(summary_ar, ''::text)) || ' '::text) || COALESCE(summary_en, ''::text))))", stored: true);
        e.HasIndex(x => new { x.Type, x.Status, x.Sort }).HasDatabaseName("ix_product_listing");
        e.HasIndex("SearchVector").HasMethod("gin").HasDatabaseName("ix_product_search");
        e.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("product_slug_key");
        e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).HasConstraintName("product_category_id_fkey").OnDelete(DeleteBehavior.SetNull);
        e.HasOne<Ta3lom.Store.Domain.Media.MediaAsset>().WithMany().HasForeignKey(x => x.CoverAssetId).HasConstraintName("product_cover_asset_id_fkey").OnDelete(DeleteBehavior.SetNull);
    }
}
