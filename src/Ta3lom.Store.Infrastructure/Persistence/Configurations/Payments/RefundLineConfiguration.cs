using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Payments;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Payments;

/// `refund_line` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class RefundLineConfiguration : IEntityTypeConfiguration<RefundLine>
{
    public void Configure(EntityTypeBuilder<RefundLine> e)
    {
        e.ToTable("refund_line", t =>
        {
            t.HasCheckConstraint("refund_line_amount_check", "(amount > (0)::numeric)");
        });
        e.HasKey(x => new { x.RefundId, x.OrderLineId }).HasName("refund_line_pkey");
        e.Property(x => x.RefundId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.OrderLineId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Amount).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Units).HasColumnType("integer").IsRequired(false).ValueGeneratedNever();
        e.HasOne<Ta3lom.Store.Domain.Ordering.OrderLine>().WithMany().HasForeignKey(x => x.OrderLineId).HasConstraintName("refund_line_order_line_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Refund>().WithMany().HasForeignKey(x => x.RefundId).HasConstraintName("refund_line_refund_id_fkey").OnDelete(DeleteBehavior.Cascade);
    }
}
