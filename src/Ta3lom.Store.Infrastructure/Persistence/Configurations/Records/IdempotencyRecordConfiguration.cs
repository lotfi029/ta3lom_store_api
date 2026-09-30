using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Infrastructure.Persistence.Records;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Records;

/// `idempotency_key` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> e)
    {
        e.ToTable("idempotency_key");
        e.HasKey(x => new { x.Scope, x.Key }).HasName("idempotency_key_pkey");
        e.Property(x => x.Key).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Scope).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.RequestHash).HasColumnType("character(64)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.StatusCode).HasColumnType("integer").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Response).HasColumnType("jsonb").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_idempotency_created");
    }
}
