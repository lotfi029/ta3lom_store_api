using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Settings;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Settings;

/// `setting`: one JSON value per key.
internal sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> e)
    {
        e.ToTable("setting");
        e.HasKey(x => x.Key).HasName("pk_setting");
        e.Property(x => x.Key).HasColumnType("text");
        e.Property(x => x.Value).HasColumnType("jsonb").IsRequired();
        e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.UpdatedBy).HasColumnType("text");
    }
}
