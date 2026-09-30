using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Payments;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Payments;

/// `payment` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> e)
    {
        e.ToTable("payment", t =>
        {
            t.HasCheckConstraint("payment_amount_check", "(amount > (0)::numeric)");
            t.HasCheckConstraint("payment_method_check", "(method = ANY (ARRAY['mada'::text, 'applepay'::text, 'card'::text, 'stcpay'::text, 'bnpl'::text]))");
            t.HasCheckConstraint("payment_status_check", "(status = ANY (ARRAY['initiated'::text, 'authorized'::text, 'captured'::text, 'failed'::text, 'voided'::text, 'refunded'::text, 'partially_refunded'::text]))");
        });
        e.HasKey(x => x.Id).HasName("payment_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.OrderId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Provider).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.ProviderPaymentId).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Method).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Amount).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Status).HasColumnType("text").IsRequired().HasDefaultValueSql("'initiated'::text").ValueGeneratedNever();
        e.Property(x => x.FailureCode).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.FailureMessage).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.HasIndex(x => x.OrderId).HasDatabaseName("ix_payment_order");
        e.HasIndex(x => new { x.Status, x.CreatedAt }).HasFilter("(status = 'initiated'::text)").HasDatabaseName("ix_payment_reconcile");
        e.HasIndex(x => new { x.Provider, x.ProviderPaymentId }).IsUnique().HasDatabaseName("payment_provider_provider_payment_id_key");
        e.HasOne<Ta3lom.Store.Domain.Entities.Ordering.Order>().WithMany().HasForeignKey(x => x.OrderId).HasConstraintName("payment_order_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
