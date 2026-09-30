using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Fulfilment;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Fulfilment;

/// `download_log` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class DownloadLogEntryConfiguration : IEntityTypeConfiguration<DownloadLogEntry>
{
    public void Configure(EntityTypeBuilder<DownloadLogEntry> e)
    {
        e.ToTable("download_log");
        e.HasKey(x => x.Id).HasName("download_log_pkey");
        e.Property(x => x.Id).HasColumnType("bigint").IsRequired().UseIdentityAlwaysColumn();
        e.Property(x => x.GrantId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.At).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.IpHash).HasColumnType("character(64)").IsRequired(false).ValueGeneratedNever();
        e.HasOne<DownloadGrant>().WithMany().HasForeignKey(x => x.GrantId).HasConstraintName("download_log_grant_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
