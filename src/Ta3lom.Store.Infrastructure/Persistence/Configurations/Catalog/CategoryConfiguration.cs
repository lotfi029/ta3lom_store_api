using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Catalog;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Catalog;

/// `category` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> e)
    {
        e.ToTable("category", t =>
        {
            t.HasCheckConstraint("category_slug_check", "(slug ~ '^[a-z0-9-]{2,60}$'::text)");
        });
        e.HasKey(x => x.Id).HasName("category_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.Slug).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.NameAr).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.NameEn).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Sort).HasColumnType("integer").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
        e.Property(x => x.IsActive).HasColumnType("boolean").IsRequired().HasDefaultValueSql("true").ValueGeneratedNever();
        e.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("category_slug_key");
    }
}
