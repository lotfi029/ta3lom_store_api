using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Settings;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Settings;

/// `setting` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> e)
    {
        e.ToTable("setting");
        e.HasKey(x => x.Key).HasName("setting_pkey");
        e.Property(x => x.Key).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Value).HasColumnType("jsonb").IsRequired().ValueGeneratedNever();
        e.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.UpdatedBy).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
    }
}
