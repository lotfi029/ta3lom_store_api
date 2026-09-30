using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Catalog;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Catalog;

/// `sponsorship_offer` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class SponsorshipOfferConfiguration : IEntityTypeConfiguration<SponsorshipOffer>
{
    public void Configure(EntityTypeBuilder<SponsorshipOffer> e)
    {
        e.ToTable("sponsorship_offer", t =>
        {
            t.HasCheckConstraint("sponsorship_offer_check", "(ends_at > starts_at)");
            t.HasCheckConstraint("sponsorship_offer_seat_price_check", "(seat_price > (0)::numeric)");
            t.HasCheckConstraint("sponsorship_offer_target_units_check", "(target_units > 0)");
        });
        e.HasKey(x => x.Id).HasName("sponsorship_offer_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.ProductId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.SeatPrice).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.UnitLabelAr).HasColumnType("text").IsRequired().HasDefaultValueSql("'مقعد'::text").ValueGeneratedNever();
        e.Property(x => x.UnitLabelEn).HasColumnType("text").IsRequired().HasDefaultValueSql("'seat'::text").ValueGeneratedNever();
        e.Property(x => x.TargetUnits).HasColumnType("integer").IsRequired().ValueGeneratedNever();
        e.Property(x => x.BeneficiaryGroup).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.GroupLabelAr).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.GroupLabelEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.AllowPartial).HasColumnType("boolean").IsRequired().HasDefaultValueSql("true").ValueGeneratedNever();
        e.Property(x => x.ZakatEligible).HasColumnType("boolean").IsRequired().HasDefaultValueSql("false").ValueGeneratedNever();
        e.Property(x => x.CloseOnTarget).HasColumnType("boolean").IsRequired().HasDefaultValueSql("false").ValueGeneratedNever();
        e.Property(x => x.StartsAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.EndsAt).HasColumnType("timestamp with time zone").IsRequired().ValueGeneratedNever();
        e.Property(x => x.IsActive).HasColumnType("boolean").IsRequired().HasDefaultValueSql("true").ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.HasIndex(x => x.ProductId).IsUnique().HasFilter("is_active").HasDatabaseName("ux_offer_one_active");
        e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).HasConstraintName("sponsorship_offer_product_id_fkey").OnDelete(DeleteBehavior.Cascade);
    }
}
