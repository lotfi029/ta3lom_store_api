using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Identity;

/// `otp_challenge` (D-10): the HMAC of the code only; deleted after 24 h (D-14).
internal sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> e)
    {
        e.ToTable("otp_challenge", t =>
        {
            t.HasCheckConstraint("ck_otp_purpose", "purpose IN ('login')");
            t.HasCheckConstraint("ck_otp_attempts", "attempts BETWEEN 0 AND 5");
        });
        e.HasKey(x => x.Id).HasName("pk_otp_challenge");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Mobile).HasColumnType("text").IsRequired();
        e.Property(x => x.Purpose).HasColumnType("text").IsRequired();
        e.Property(x => x.CodeHash).HasColumnType("character(64)").IsRequired();
        e.Property(x => x.Attempts).HasColumnType("smallint").IsRequired();
        e.Property(x => x.IpHash).HasColumnType("character(64)");
        e.HasIndex(x => new { x.Mobile, x.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_otp_mobile_recent");
        e.HasIndex(x => new { x.IpHash, x.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_otp_ip_recent");
        e.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_otp_created");
    }
}
