using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Giving;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Giving;

/// `pool_contribution` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class PoolContributionConfiguration : IEntityTypeConfiguration<PoolContribution>
{
    public void Configure(EntityTypeBuilder<PoolContribution> e)
    {
        e.ToTable("pool_contribution", t =>
        {
            t.HasCheckConstraint("pool_contribution_amount_check", "(amount > (0)::numeric)");
            t.HasCheckConstraint("pool_contribution_check", "(remaining <= amount)");
            t.HasCheckConstraint("pool_contribution_remaining_check", "(remaining >= (0)::numeric)");
        });
        e.HasKey(x => x.Id).HasName("pool_contribution_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.SponsorshipOfferId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.OrderLineId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Amount).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Remaining).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.IsZakat).HasColumnType("boolean").IsRequired().HasDefaultValueSql("false").ValueGeneratedNever();
        e.Property(x => x.SweptToFundAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.HasIndex(x => new { x.SponsorshipOfferId, x.CreatedAt }).HasFilter("(remaining > (0)::numeric)").HasDatabaseName("ix_pool_fifo");
        e.HasIndex(x => x.OrderLineId).IsUnique().HasDatabaseName("pool_contribution_order_line_id_key");
        e.HasOne<Ta3lom.Store.Domain.Ordering.OrderLine>().WithMany().HasForeignKey(x => x.OrderLineId).HasConstraintName("pool_contribution_order_line_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Catalog.SponsorshipOffer>().WithMany().HasForeignKey(x => x.SponsorshipOfferId).HasConstraintName("pool_contribution_sponsorship_offer_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
