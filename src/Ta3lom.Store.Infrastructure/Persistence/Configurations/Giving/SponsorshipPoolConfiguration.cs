using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Giving;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Giving;

/// `sponsorship_pool` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class SponsorshipPoolConfiguration : IEntityTypeConfiguration<SponsorshipPool>
{
    public void Configure(EntityTypeBuilder<SponsorshipPool> e)
    {
        e.ToTable("sponsorship_pool", t =>
        {
            t.HasCheckConstraint("sponsorship_pool_balance_check", "(balance >= (0)::numeric)");
        });
        e.HasKey(x => x.SponsorshipOfferId).HasName("sponsorship_pool_pkey");
        e.Property(x => x.SponsorshipOfferId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Balance).HasColumnType("numeric(12,2)").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        e.HasOne<Ta3lom.Store.Domain.Catalog.SponsorshipOffer>().WithOne().HasForeignKey<SponsorshipPool>(x => x.SponsorshipOfferId).HasConstraintName("sponsorship_pool_sponsorship_offer_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
