using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Identity;

/// `staff_user` (BR-A11, D-12). `xmin` is the concurrency token.
internal sealed class StaffUserConfiguration : IEntityTypeConfiguration<StaffUser>
{
    public void Configure(EntityTypeBuilder<StaffUser> e)
    {
        e.ToTable("staff_user", t =>
        {
            t.HasCheckConstraint("ck_staff_roles", "cardinality(roles) > 0 AND roles <@ ARRAY['admin', 'store_manager', 'finance', 'viewer']::text[]");
        });
        e.HasKey(x => x.Id).HasName("pk_staff_user");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Email).HasColumnType("citext").IsRequired();
        e.Property(x => x.Name).HasColumnType("text").IsRequired();
        e.Property(x => x.PassHash).HasColumnType("text").IsRequired();
        e.Property(x => x.MustChangePassword).HasDefaultValue(true).ValueGeneratedNever();
        e.Property(x => x.TotpSecretEnc).HasColumnType("bytea");
        e.Property(x => x.Roles).HasColumnType("text[]").IsRequired();
        e.Property(x => x.FailedCount).HasDefaultValue(0).ValueGeneratedNever();
        e.Property(x => x.IsActive).HasDefaultValue(true).ValueGeneratedNever();
        e.Property(x => x.Version).IsRowVersion();
        e.HasIndex(x => x.Email).IsUnique().HasDatabaseName("ux_staff_email");
    }
}
