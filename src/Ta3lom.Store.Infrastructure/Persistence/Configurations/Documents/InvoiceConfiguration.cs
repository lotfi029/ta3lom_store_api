using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Documents;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Documents;

/// `invoice` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> e)
    {
        e.ToTable("invoice", t =>
        {
            t.HasCheckConstraint("invoice_check", "(gross_total = (net_total + vat_total))");
            t.HasCheckConstraint("invoice_kind_check", "(kind = ANY (ARRAY['invoice'::text, 'credit_note'::text]))");
            t.HasCheckConstraint("invoice_zatca_status_check", "(zatca_status = ANY (ARRAY['not_required'::text, 'pending'::text, 'reported'::text, 'cleared'::text, 'rejected'::text]))");
        });
        e.HasKey(x => x.Id).HasName("invoice_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.OrderId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Kind).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Number).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.IssuedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.IsTaxInvoice).HasColumnType("boolean").IsRequired().ValueGeneratedNever();
        e.Property(x => x.SellerVatNo).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.NetTotal).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.VatTotal).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.GrossTotal).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.ReferencesInvoiceId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.PdfAssetId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.ZatcaUuid).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.ZatcaIcv).HasColumnType("bigint").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.ZatcaPih).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.ZatcaHash).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.ZatcaQr).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.ZatcaStatus).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.HasIndex(x => x.Number).IsUnique().HasDatabaseName("invoice_number_key");
        e.HasIndex(x => x.OrderId).HasDatabaseName("ix_invoice_order");
        e.HasOne<Ta3lom.Store.Domain.Ordering.Order>().WithMany().HasForeignKey(x => x.OrderId).HasConstraintName("invoice_order_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Media.MediaAsset>().WithMany().HasForeignKey(x => x.PdfAssetId).HasConstraintName("invoice_pdf_asset_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Invoice>().WithMany().HasForeignKey(x => x.ReferencesInvoiceId).HasConstraintName("invoice_references_invoice_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
