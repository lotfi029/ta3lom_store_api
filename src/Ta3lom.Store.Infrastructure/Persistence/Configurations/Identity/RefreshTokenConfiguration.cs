using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Identity;

/// `refresh_token` (D-11): polymorphic subject (no FK), absolute family expiry.
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> e)
    {
        e.ToTable("refresh_token", t =>
        {
            t.HasCheckConstraint("ck_refresh_subject", "subject_type IN ('customer', 'staff')");
            t.HasCheckConstraint("ck_refresh_expiry", "expires_at <= family_expires_at");
        });
        e.HasKey(x => x.Id).HasName("pk_refresh_token");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.SubjectType).HasColumnType("text").IsRequired();
        e.Property(x => x.TokenHash).HasColumnType("character(64)").IsRequired();
        e.Property(x => x.UserAgent).HasColumnType("text");
        e.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_refresh_token_hash");
        e.HasIndex(x => new { x.SubjectType, x.SubjectId }).HasDatabaseName("ix_refresh_subject");
        e.HasIndex(x => x.FamilyId).HasDatabaseName("ix_refresh_family");
    }
}
