using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Catalog;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Catalog;

/// `course_module` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class CourseModuleConfiguration : IEntityTypeConfiguration<CourseModule>
{
    public void Configure(EntityTypeBuilder<CourseModule> e)
    {
        e.ToTable("course_module");
        e.HasKey(x => x.Id).HasName("course_module_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.ProductId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Sort).HasColumnType("integer").IsRequired().ValueGeneratedNever();
        e.Property(x => x.TitleAr).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.TitleEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.DurationSeconds).HasColumnType("integer").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.HasIndex(x => new { x.ProductId, x.Sort }).IsUnique().HasDatabaseName("course_module_product_id_sort_key");
        e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).HasConstraintName("course_module_product_id_fkey").OnDelete(DeleteBehavior.Cascade);
    }
}
