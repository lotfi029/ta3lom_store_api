using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Giving;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Giving;

/// `unit_funding` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class UnitFundingConfiguration : IEntityTypeConfiguration<UnitFunding>
{
    public void Configure(EntityTypeBuilder<UnitFunding> e)
    {
        e.ToTable("unit_funding", t =>
        {
            t.HasCheckConstraint("unit_funding_amount_check", "(amount > (0)::numeric)");
        });
        e.HasKey(x => new { x.SponsoredUnitId, x.PoolContributionId }).HasName("unit_funding_pkey");
        e.Property(x => x.SponsoredUnitId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.PoolContributionId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Amount).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.HasOne<PoolContribution>().WithMany().HasForeignKey(x => x.PoolContributionId).HasConstraintName("unit_funding_pool_contribution_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<SponsoredUnit>().WithMany().HasForeignKey(x => x.SponsoredUnitId).HasConstraintName("unit_funding_sponsored_unit_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
