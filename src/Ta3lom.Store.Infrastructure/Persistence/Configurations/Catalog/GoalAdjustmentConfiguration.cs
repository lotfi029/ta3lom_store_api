using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Catalog;

/// `goal_adjustment` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class GoalAdjustmentConfiguration : IEntityTypeConfiguration<GoalAdjustment>
{
    public void Configure(EntityTypeBuilder<GoalAdjustment> e)
    {
        e.ToTable("goal_adjustment", t =>
        {
            t.HasCheckConstraint("goal_adjustment_check", "((amount <> (0)::numeric) OR (units <> 0))");
            t.HasCheckConstraint("goal_adjustment_kind_check", "(kind = ANY (ARRAY['pool_sweep_out'::text, 'pool_sweep_in'::text, 'correction'::text]))");
        });
        e.HasKey(x => x.Id).HasName("goal_adjustment_pkey");
        e.Property(x => x.Id).HasColumnType("bigint").IsRequired().UseIdentityAlwaysColumn();
        e.Property(x => x.ProductId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Kind).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Amount).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Units).HasColumnType("integer").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.SponsorshipOfferId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.PairId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Note).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.At).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.Actor).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.HasIndex(x => x.ProductId).HasDatabaseName("ix_goal_adjustment_product");
        e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).HasConstraintName("goal_adjustment_product_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<SponsorshipOffer>().WithMany().HasForeignKey(x => x.SponsorshipOfferId).HasConstraintName("goal_adjustment_sponsorship_offer_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
