using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Infrastructure.Persistence.Records;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Records;

/// `idempotency_key`: (scope, key) → the stored response; pruned after `expires_at` (24 h, D-14).
internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> e)
    {
        e.ToTable("idempotency_key", t => t.HasCheckConstraint("ck_idempotency_status", "status IN ('in_progress', 'completed')"));
        e.HasKey(x => new { x.Scope, x.Key }).HasName("pk_idempotency_key");
        e.Property(x => x.Scope).HasColumnType("text");
        e.Property(x => x.Key).HasColumnType("text");
        e.Property(x => x.RequestHash).HasColumnType("character(64)").IsRequired();
        e.Property(x => x.Status).HasColumnType("text").IsRequired();
        e.Property(x => x.ResponseBody).HasColumnType("jsonb");
        e.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_idempotency_expires");
    }
}
