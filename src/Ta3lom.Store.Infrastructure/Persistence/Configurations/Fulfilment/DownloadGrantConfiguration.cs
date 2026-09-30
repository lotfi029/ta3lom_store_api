using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Fulfilment;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Fulfilment;

/// `download_grant` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class DownloadGrantConfiguration : IEntityTypeConfiguration<DownloadGrant>
{
    public void Configure(EntityTypeBuilder<DownloadGrant> e)
    {
        e.ToTable("download_grant", t =>
        {
            t.HasCheckConstraint("download_grant_check", "(download_count <= max_downloads)");
        });
        e.HasKey(x => x.Id).HasName("download_grant_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.CustomerId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.OrderLineId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.BookFormatId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.MaxDownloads).HasColumnType("integer").IsRequired().HasDefaultValueSql("5").ValueGeneratedNever();
        e.Property(x => x.DownloadCount).HasColumnType("integer").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.RevokedAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.HasIndex(x => x.OrderLineId).IsUnique().HasDatabaseName("download_grant_order_line_id_key");
        e.HasOne<Ta3lom.Store.Domain.Catalog.BookFormat>().WithMany().HasForeignKey(x => x.BookFormatId).HasConstraintName("download_grant_book_format_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Identity.Customer>().WithMany().HasForeignKey(x => x.CustomerId).HasConstraintName("download_grant_customer_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Ordering.OrderLine>().WithMany().HasForeignKey(x => x.OrderLineId).HasConstraintName("download_grant_order_line_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
