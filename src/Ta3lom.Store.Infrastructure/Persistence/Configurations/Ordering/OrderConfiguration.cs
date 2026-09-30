using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Ordering;

/// `orders` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> e)
    {
        e.ToTable("orders", t =>
        {
            t.HasCheckConstraint("ck_order_total", "(grand_total = ((purchases_total + shipping_total) + gifts_total))");
            t.HasCheckConstraint("orders_currency_check", "(currency = 'SAR'::bpchar)");
            t.HasCheckConstraint("orders_grand_total_check", "(grand_total > (0)::numeric)");
            t.HasCheckConstraint("orders_status_check", "(status = ANY (ARRAY['pending_payment'::text, 'paid'::text, 'payment_failed'::text, 'payment_review'::text, 'expired'::text, 'cancelled'::text, 'fulfilled'::text, 'partially_refunded'::text, 'refunded'::text]))");
        });
        e.HasKey(x => x.Id).HasName("orders_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.Reference).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.CustomerId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Status).HasColumnType("text").IsRequired().HasDefaultValueSql("'pending_payment'::text").ValueGeneratedNever();
        e.Property(x => x.Currency).HasColumnType("character(3)").IsRequired().HasDefaultValueSql("'SAR'::bpchar").ValueGeneratedNever();
        e.Property(x => x.PurchasesTotal).HasColumnType("numeric(12,2)").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.ShippingTotal).HasColumnType("numeric(12,2)").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.GiftsTotal).HasColumnType("numeric(12,2)").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.VatTotal).HasColumnType("numeric(12,2)").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.GrandTotal).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.RefundedTotal).HasColumnType("numeric(12,2)").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.IsAnonymous).HasColumnType("boolean").IsRequired().HasDefaultValueSql("false").ValueGeneratedNever();
        e.Property(x => x.Locale).HasColumnType("character(2)").IsRequired().HasDefaultValueSql("'ar'::bpchar").ValueGeneratedNever();
        e.Property(x => x.ContactName).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.ContactMobile).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.ContactEmail).HasColumnType("citext").IsRequired().ValueGeneratedNever();
        e.Property(x => x.ShippingAddress).HasColumnType("jsonb").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.SourceCartId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.TermsAcceptedAt).HasColumnType("timestamp with time zone").IsRequired().ValueGeneratedNever();
        e.Property(x => x.PlacedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.PaymentDueAt).HasColumnType("timestamp with time zone").IsRequired().ValueGeneratedNever();
        e.Property(x => x.PaidAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.FulfilledAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CancelledAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        e.HasIndex(x => new { x.CustomerId, x.PlacedAt }).IsDescending(false, true).HasDatabaseName("ix_order_customer");
        e.HasIndex(x => x.PaymentDueAt).HasFilter("(status = ANY (ARRAY['pending_payment'::text, 'payment_failed'::text]))").HasDatabaseName("ix_order_due");
        e.HasIndex(x => new { x.Status, x.PlacedAt }).HasDatabaseName("ix_order_status");
        e.HasIndex(x => x.Reference).IsUnique().HasDatabaseName("orders_reference_key");
        e.HasOne<Ta3lom.Store.Domain.Entities.Identity.Customer>().WithMany().HasForeignKey(x => x.CustomerId).HasConstraintName("orders_customer_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
