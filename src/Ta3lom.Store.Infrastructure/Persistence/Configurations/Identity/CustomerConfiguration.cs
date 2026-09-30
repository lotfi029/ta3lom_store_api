using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Identity;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Identity;

/// `customer` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> e)
    {
        e.ToTable("customer", t =>
        {
            t.HasCheckConstraint("customer_locale_check", "(locale = ANY (ARRAY['ar'::bpchar, 'en'::bpchar]))");
            t.HasCheckConstraint("customer_mobile_check", "(mobile ~ '^\\+9665[0-9]{8}$'::text)");
            t.HasCheckConstraint("customer_status_check", "(status = ANY (ARRAY['active'::text, 'blocked'::text, 'anonymised'::text]))");
        });
        e.HasKey(x => x.Id).HasName("customer_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.Mobile).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.FullName).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Email).HasColumnType("citext").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Locale).HasColumnType("character(2)").IsRequired().HasDefaultValueSql("'ar'::bpchar").ValueGeneratedNever();
        e.Property(x => x.AnonymousDefault).HasColumnType("boolean").IsRequired().HasDefaultValueSql("false").ValueGeneratedNever();
        e.Property(x => x.MarketingOptIn).HasColumnType("boolean").IsRequired().HasDefaultValueSql("false").ValueGeneratedNever();
        e.Property(x => x.Status).HasColumnType("text").IsRequired().HasDefaultValueSql("'active'::text").ValueGeneratedNever();
        e.Property(x => x.PdplConsentAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.LastLoginAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.HasIndex(x => x.Mobile).IsUnique().HasDatabaseName("customer_mobile_key");
        e.HasIndex(x => x.Email).HasDatabaseName("ix_customer_email");
    }
}
