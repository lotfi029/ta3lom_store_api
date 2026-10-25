using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Auditing;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Auditing;

/// `audit_log` (BR-A13): append-only, `bigint identity`.
internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> e)
    {
        e.ToTable("audit_log");
        e.HasKey(x => x.Id).HasName("pk_audit_log");
        e.Property(x => x.Id).UseIdentityAlwaysColumn();
        e.Property(x => x.At).IsRequired();
        e.Property(x => x.Actor).HasColumnType("text").IsRequired();
        e.Property(x => x.Action).HasColumnType("text").IsRequired();
        e.Property(x => x.Entity).HasColumnType("text").IsRequired();
        e.Property(x => x.EntityId).HasColumnType("text");
        e.Property(x => x.Diff).HasColumnType("jsonb");
        e.Property(x => x.IpHash).HasColumnType("character(64)");
        e.HasIndex(x => x.At).IsDescending().HasDatabaseName("ix_audit_at");
        e.HasIndex(x => new { x.Entity, x.EntityId }).HasDatabaseName("ix_audit_entity");
        e.HasIndex(x => x.Action).HasDatabaseName("ix_audit_action");
    }
}
