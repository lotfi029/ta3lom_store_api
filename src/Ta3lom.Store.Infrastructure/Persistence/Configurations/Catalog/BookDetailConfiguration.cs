using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Catalog;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Catalog;

/// `book_detail` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class BookDetailConfiguration : IEntityTypeConfiguration<BookDetail>
{
    public void Configure(EntityTypeBuilder<BookDetail> e)
    {
        e.ToTable("book_detail", t =>
        {
            t.HasCheckConstraint("book_detail_pages_check", "(pages > 0)");
        });
        e.HasKey(x => x.ProductId).HasName("book_detail_pkey");
        e.Property(x => x.ProductId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.AuthorAr).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.AuthorEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Pages).HasColumnType("integer").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Isbn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.PublishedYear).HasColumnType("integer").IsRequired(false).ValueGeneratedNever();
        e.HasOne<Product>().WithOne().HasForeignKey<BookDetail>(x => x.ProductId).HasConstraintName("book_detail_product_id_fkey").OnDelete(DeleteBehavior.Cascade);
    }
}
