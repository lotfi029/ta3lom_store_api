using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Identity;

/// `staff_user` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class StaffUserConfiguration : IEntityTypeConfiguration<StaffUser>
{
    public void Configure(EntityTypeBuilder<StaffUser> e)
    {
        e.ToTable("staff_user", t =>
        {
            t.HasCheckConstraint("staff_user_roles_check", "(roles <@ ARRAY['admin'::text, 'store_manager'::text, 'finance'::text, 'allocation_officer'::text, 'fulfilment'::text, 'viewer'::text])");
        });
        e.HasKey(x => x.Id).HasName("staff_user_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.Email).HasColumnType("citext").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Name).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.PassHash).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.TotpSecretEnc).HasColumnType("bytea").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Roles).HasColumnType("text[]").IsRequired().HasDefaultValueSql("'{viewer}'::text[]").ValueGeneratedNever();
        e.Property(x => x.FailedCount).HasColumnType("integer").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.LockedUntil).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.IsActive).HasColumnType("boolean").IsRequired().HasDefaultValueSql("true").ValueGeneratedNever();
        e.Property(x => x.LastLoginAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.HasIndex(x => x.Email).IsUnique().HasDatabaseName("staff_user_email_key");
    }
}
