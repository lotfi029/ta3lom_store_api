using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Ordering;

/// `order_line` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> e)
    {
        e.ToTable("order_line", t =>
        {
            t.HasCheckConstraint("ck_line_buy_vat", "((kind = 'buy'::text) OR (vat_amount = (0)::numeric))");
            t.HasCheckConstraint("ck_line_intent", "((kind = 'donate'::text) OR (kind = 'sponsor'::text) OR (intent IS NULL))");
            t.HasCheckConstraint("ck_line_offer", "((kind <> 'sponsor'::text) OR (sponsorship_offer_id IS NOT NULL))");
            t.HasCheckConstraint("ck_line_refund", "(refunded_amount <= amount)");
            t.HasCheckConstraint("order_line_amount_check", "(amount > (0)::numeric)");
            t.HasCheckConstraint("order_line_fulfilment_status_check", "(fulfilment_status = ANY (ARRAY['pending'::text, 'enrolled'::text, 'pending_sync'::text, 'available'::text, 'preparing'::text, 'shipped'::text, 'delivered'::text, 'returned'::text, 'funded'::text, 'received'::text, 'refunded'::text]))");
            t.HasCheckConstraint("order_line_intent_check", "(intent = ANY (ARRAY['sadaqa'::text, 'zakat'::text]))");
            t.HasCheckConstraint("order_line_kind_check", "(kind = ANY (ARRAY['buy'::text, 'sponsor'::text, 'donate'::text]))");
            t.HasCheckConstraint("order_line_mode_check", "(mode = ANY (ARRAY['units'::text, 'amount'::text]))");
            t.HasCheckConstraint("order_line_product_type_check", "(product_type = ANY (ARRAY['course'::text, 'book'::text, 'cause'::text]))");
            t.HasCheckConstraint("order_line_quantity_check", "((quantity >= 1) AND (quantity <= 999))");
        });
        e.HasKey(x => x.Id).HasName("order_line_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.OrderId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.LineNo).HasColumnType("smallint").IsRequired().ValueGeneratedNever();
        e.Property(x => x.ProductId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.ProductType).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.BookFormatId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.SponsorshipOfferId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Kind).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Mode).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Quantity).HasColumnType("integer").IsRequired().ValueGeneratedNever();
        e.Property(x => x.UnitPrice).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Amount).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.VatRate).HasColumnType("numeric(5,2)").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.VatAmount).HasColumnType("numeric(12,2)").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.Intent).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.TitleAr).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.TitleEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.FulfilmentStatus).HasColumnType("text").IsRequired().HasDefaultValueSql("'pending'::text").ValueGeneratedNever();
        e.Property(x => x.RefundedAmount).HasColumnType("numeric(12,2)").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.HasIndex(x => new { x.ProductId, x.Kind }).HasDatabaseName("ix_order_line_product");
        e.HasIndex(x => new { x.OrderId, x.LineNo }).IsUnique().HasDatabaseName("order_line_order_id_line_no_key");
        e.HasOne<Ta3lom.Store.Domain.Entities.Catalog.BookFormat>().WithMany().HasForeignKey(x => x.BookFormatId).HasConstraintName("order_line_book_format_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).HasConstraintName("order_line_order_id_fkey").OnDelete(DeleteBehavior.Cascade);
        e.HasOne<Ta3lom.Store.Domain.Entities.Catalog.Product>().WithMany().HasForeignKey(x => x.ProductId).HasConstraintName("order_line_product_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Entities.Catalog.SponsorshipOffer>().WithMany().HasForeignKey(x => x.SponsorshipOfferId).HasConstraintName("order_line_sponsorship_offer_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
