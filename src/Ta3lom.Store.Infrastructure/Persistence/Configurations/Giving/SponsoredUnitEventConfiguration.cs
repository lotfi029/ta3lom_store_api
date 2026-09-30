using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Giving;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Giving;

/// `sponsored_unit_event` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class SponsoredUnitEventConfiguration : IEntityTypeConfiguration<SponsoredUnitEvent>
{
    public void Configure(EntityTypeBuilder<SponsoredUnitEvent> e)
    {
        e.ToTable("sponsored_unit_event");
        e.HasKey(x => x.Id).HasName("sponsored_unit_event_pkey");
        e.Property(x => x.Id).HasColumnType("bigint").IsRequired().UseIdentityAlwaysColumn();
        e.Property(x => x.UnitId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.FromStatus).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.ToStatus).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.At).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.Actor).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.HasIndex(x => new { x.UnitId, x.At }).HasDatabaseName("ix_unit_event_unit");
        e.HasOne<SponsoredUnit>().WithMany().HasForeignKey(x => x.UnitId).HasConstraintName("sponsored_unit_event_unit_id_fkey").OnDelete(DeleteBehavior.Cascade);
    }
}
