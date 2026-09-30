using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Documents;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Documents;

/// `document_counter` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class DocumentCounterConfiguration : IEntityTypeConfiguration<DocumentCounter>
{
    public void Configure(EntityTypeBuilder<DocumentCounter> e)
    {
        e.ToTable("document_counter", t =>
        {
            t.HasCheckConstraint("document_counter_series_check", "(series = ANY (ARRAY['INV'::text, 'CN'::text, 'DR'::text, 'DRC'::text]))");
        });
        e.HasKey(x => new { x.Series, x.Year }).HasName("document_counter_pkey");
        e.Property(x => x.Series).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Year).HasColumnType("integer").IsRequired().ValueGeneratedNever();
        e.Property(x => x.LastValue).HasColumnType("integer").IsRequired().HasDefaultValueSql("0").ValueGeneratedNever();
    }
}
