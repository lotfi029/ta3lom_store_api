using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Catalog;

/// `book_format` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class BookFormatConfiguration : IEntityTypeConfiguration<BookFormat>
{
    public void Configure(EntityTypeBuilder<BookFormat> e)
    {
        e.ToTable("book_format", t =>
        {
            t.HasCheckConstraint("book_format_check", "((compare_at_price IS NULL) OR (compare_at_price > price))");
            t.HasCheckConstraint("book_format_kind_check", "(kind = ANY (ARRAY['print'::text, 'digital'::text]))");
            t.HasCheckConstraint("book_format_low_stock_threshold_check", "(low_stock_threshold >= 0)");
            t.HasCheckConstraint("book_format_price_check", "(price >= (0)::numeric)");
            t.HasCheckConstraint("book_format_stock_on_hand_check", "(stock_on_hand >= 0)");
            t.HasCheckConstraint("book_format_stock_reserved_check", "(stock_reserved >= 0)");
            t.HasCheckConstraint("ck_format_digital", "((kind <> 'digital'::text) OR (file_asset_id IS NOT NULL))");
            t.HasCheckConstraint("ck_format_print", "((kind <> 'print'::text) OR (stock_on_hand IS NOT NULL))");
            t.HasCheckConstraint("ck_format_reserve", "(stock_reserved <= COALESCE(stock_on_hand, 0))");
        });
        e.HasKey(x => x.Id).HasName("book_format_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.ProductId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Kind).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Sku).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Price).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.CompareAtPrice).HasColumnType("numeric(12,2)").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.IsActive).HasColumnType("boolean").IsRequired().HasDefaultValueSql("true").ValueGeneratedNever();
        e.Property(x => x.StockOnHand).HasColumnType("integer").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.StockReserved).HasColumnType("integer").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.WeightGrams).HasColumnType("integer").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.LowStockThreshold).HasColumnType("integer").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.FileAssetId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        e.HasIndex(x => new { x.ProductId, x.Kind }).IsUnique().HasDatabaseName("book_format_product_id_kind_key");
        e.HasIndex(x => x.Sku).IsUnique().HasDatabaseName("book_format_sku_key");
        e.HasOne<Ta3lom.Store.Domain.Entities.Media.MediaAsset>().WithMany().HasForeignKey(x => x.FileAssetId).HasConstraintName("book_format_file_asset_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).HasConstraintName("book_format_product_id_fkey").OnDelete(DeleteBehavior.Cascade);
    }
}
