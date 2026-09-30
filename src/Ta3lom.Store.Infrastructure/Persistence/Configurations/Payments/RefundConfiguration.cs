using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Payments;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Payments;

/// `refund` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> e)
    {
        e.ToTable("refund", t =>
        {
            t.HasCheckConstraint("refund_amount_check", "(amount > (0)::numeric)");
            t.HasCheckConstraint("refund_status_check", "(status = ANY (ARRAY['requested'::text, 'succeeded'::text, 'failed'::text]))");
        });
        e.HasKey(x => x.Id).HasName("refund_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.OrderId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.PaymentId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Amount).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Reason).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Status).HasColumnType("text").IsRequired().HasDefaultValueSql("'requested'::text").ValueGeneratedNever();
        e.Property(x => x.ProviderRefundId).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.RequestedBy).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.CompletedAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.HasOne<Ta3lom.Store.Domain.Ordering.Order>().WithMany().HasForeignKey(x => x.OrderId).HasConstraintName("refund_order_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).HasConstraintName("refund_payment_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
