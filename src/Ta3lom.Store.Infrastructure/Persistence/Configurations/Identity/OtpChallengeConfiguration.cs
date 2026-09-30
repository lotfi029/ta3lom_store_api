using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Identity;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Identity;

/// `otp_challenge` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> e)
    {
        e.ToTable("otp_challenge", t =>
        {
            t.HasCheckConstraint("otp_challenge_purpose_check", "(purpose = ANY (ARRAY['login'::text, 'beneficiary_invite'::text]))");
        });
        e.HasKey(x => x.Id).HasName("otp_challenge_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.Mobile).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Purpose).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.CodeHash).HasColumnType("character(64)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Attempts).HasColumnType("smallint").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired().ValueGeneratedNever();
        e.Property(x => x.ConsumedAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.IpHash).HasColumnType("character(64)").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.HasIndex(x => new { x.Mobile, x.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_otp_mobile_recent");
    }
}
