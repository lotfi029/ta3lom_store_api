using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Giving;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Giving;

/// `allocation_batch` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class AllocationBatchConfiguration : IEntityTypeConfiguration<AllocationBatch>
{
    public void Configure(EntityTypeBuilder<AllocationBatch> e)
    {
        e.ToTable("allocation_batch");
        e.HasKey(x => x.Id).HasName("allocation_batch_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.ProductId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Note).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CreatedBy).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.HasOne<Ta3lom.Store.Domain.Entities.Catalog.Product>().WithMany().HasForeignKey(x => x.ProductId).HasConstraintName("allocation_batch_product_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
