using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Identity;

/// `refresh_token` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> e)
    {
        e.ToTable("refresh_token", t =>
        {
            t.HasCheckConstraint("refresh_token_subject_type_check", "(subject_type = ANY (ARRAY['customer'::text, 'staff'::text]))");
        });
        e.HasKey(x => x.Id).HasName("refresh_token_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.SubjectType).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.SubjectId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.TokenHash).HasColumnType("character(64)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.FamilyId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired().ValueGeneratedNever();
        e.Property(x => x.RevokedAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.ReplacedBy).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.UserAgent).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.HasIndex(x => new { x.SubjectType, x.SubjectId }).HasDatabaseName("ix_refresh_subject");
        e.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("refresh_token_token_hash_key");
    }
}
