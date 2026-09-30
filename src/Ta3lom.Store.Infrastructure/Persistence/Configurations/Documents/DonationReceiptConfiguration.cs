using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Documents;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Documents;

/// `donation_receipt` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class DonationReceiptConfiguration : IEntityTypeConfiguration<DonationReceipt>
{
    public void Configure(EntityTypeBuilder<DonationReceipt> e)
    {
        e.ToTable("donation_receipt", t =>
        {
            t.HasCheckConstraint("donation_receipt_kind_check", "(kind = ANY (ARRAY['receipt'::text, 'cancellation'::text]))");
        });
        e.HasKey(x => x.Id).HasName("donation_receipt_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.OrderId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Kind).HasColumnType("text").IsRequired().HasDefaultValueSql("'receipt'::text").ValueGeneratedNever();
        e.Property(x => x.Number).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.IssuedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.DonorName).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.TotalAmount).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.ZakatAmount).HasColumnType("numeric(12,2)").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.ReferencesReceiptId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.PdfAssetId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.HasIndex(x => x.Number).IsUnique().HasDatabaseName("donation_receipt_number_key");
        e.HasIndex(x => x.OrderId).HasDatabaseName("ix_receipt_order");
        e.HasOne<Ta3lom.Store.Domain.Ordering.Order>().WithMany().HasForeignKey(x => x.OrderId).HasConstraintName("donation_receipt_order_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Media.MediaAsset>().WithMany().HasForeignKey(x => x.PdfAssetId).HasConstraintName("donation_receipt_pdf_asset_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<DonationReceipt>().WithMany().HasForeignKey(x => x.ReferencesReceiptId).HasConstraintName("donation_receipt_references_receipt_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
