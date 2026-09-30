using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Giving;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Giving;

/// `zakat_ledger` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class ZakatLedgerEntryConfiguration : IEntityTypeConfiguration<ZakatLedgerEntry>
{
    public void Configure(EntityTypeBuilder<ZakatLedgerEntry> e)
    {
        e.ToTable("zakat_ledger", t =>
        {
            t.HasCheckConstraint("zakat_ledger_amount_check", "(amount > (0)::numeric)");
            t.HasCheckConstraint("zakat_ledger_direction_check", "(direction = ANY (ARRAY['in'::text, 'out'::text, 'refund'::text]))");
        });
        e.HasKey(x => x.Id).HasName("zakat_ledger_pkey");
        e.Property(x => x.Id).HasColumnType("bigint").IsRequired().UseIdentityAlwaysColumn();
        e.Property(x => x.ProductId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Direction).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Amount).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.OrderLineId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Reference).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.At).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.Actor).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.HasIndex(x => new { x.ProductId, x.At }).HasDatabaseName("ix_zakat_product");
        e.HasOne<Ta3lom.Store.Domain.Ordering.OrderLine>().WithMany().HasForeignKey(x => x.OrderLineId).HasConstraintName("zakat_ledger_order_line_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Catalog.Product>().WithMany().HasForeignKey(x => x.ProductId).HasConstraintName("zakat_ledger_product_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
