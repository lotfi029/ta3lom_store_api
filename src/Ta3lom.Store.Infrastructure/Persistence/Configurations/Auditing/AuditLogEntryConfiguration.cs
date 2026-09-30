using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Auditing;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Auditing;

/// `audit_log` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> e)
    {
        e.ToTable("audit_log");
        e.HasKey(x => x.Id).HasName("audit_log_pkey");
        e.Property(x => x.Id).HasColumnType("bigint").IsRequired().UseIdentityAlwaysColumn();
        e.Property(x => x.At).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.Actor).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Action).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Entity).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.EntityId).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Diff).HasColumnType("jsonb").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.IpHash).HasColumnType("character(64)").IsRequired(false).ValueGeneratedNever();
        e.HasIndex(x => x.At).IsDescending(true).HasDatabaseName("ix_audit_at");
        e.HasIndex(x => new { x.Entity, x.EntityId }).HasDatabaseName("ix_audit_entity");
    }
}
