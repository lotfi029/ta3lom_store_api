using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Identity;

/// `customer` (BR-A09, BR-A10, D-13, D-14). `xmin` is the concurrency token.
internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> e)
    {
        e.ToTable("customer", t =>
        {
            t.HasCheckConstraint("ck_customer_mobile", "mobile ~ '^\\+9665[0-9]{8}$'");
            t.HasCheckConstraint("ck_customer_locale", "locale IN ('ar', 'en')");
            t.HasCheckConstraint("ck_customer_status", "status IN ('active', 'blocked', 'anonymised')");
            t.HasCheckConstraint("ck_customer_mobile_present", "status = 'anonymised' OR mobile IS NOT NULL");
        });
        e.HasKey(x => x.Id).HasName("pk_customer");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Mobile).HasColumnType("text");
        e.Property(x => x.FullName).HasColumnType("text");
        e.Property(x => x.Email).HasColumnType("citext");
        e.Property(x => x.Locale).HasColumnType("character(2)").IsRequired();
        e.Property(x => x.MarketingOptIn).HasDefaultValue(false).ValueGeneratedNever();
        e.Property(x => x.Status).HasColumnType("text").IsRequired();
        e.Property(x => x.Version).IsRowVersion();
        e.HasIndex(x => x.Mobile).IsUnique().HasFilter("mobile IS NOT NULL").HasDatabaseName("ux_customer_mobile");
        e.HasIndex(x => x.Email).HasDatabaseName("ix_customer_email");
    }
}
