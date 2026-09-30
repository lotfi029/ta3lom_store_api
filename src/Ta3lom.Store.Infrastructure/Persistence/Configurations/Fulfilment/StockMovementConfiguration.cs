using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Fulfilment;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Fulfilment;

/// `stock_movement` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> e)
    {
        e.ToTable("stock_movement", t =>
        {
            t.HasCheckConstraint("stock_movement_delta_check", "(delta <> 0)");
            t.HasCheckConstraint("stock_movement_reason_check", "(reason = ANY (ARRAY['restock'::text, 'sale'::text, 'return'::text, 'sponsor_distribution'::text, 'adjustment'::text]))");
        });
        e.HasKey(x => x.Id).HasName("stock_movement_pkey");
        e.Property(x => x.Id).HasColumnType("bigint").IsRequired().UseIdentityAlwaysColumn();
        e.Property(x => x.BookFormatId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Delta).HasColumnType("integer").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Reason).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.RefType).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.RefId).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Note).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.At).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.Actor).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.HasIndex(x => new { x.BookFormatId, x.At }).IsDescending(false, true).HasDatabaseName("ix_stock_movement_format");
        e.HasOne<Ta3lom.Store.Domain.Entities.Catalog.BookFormat>().WithMany().HasForeignKey(x => x.BookFormatId).HasConstraintName("stock_movement_book_format_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
