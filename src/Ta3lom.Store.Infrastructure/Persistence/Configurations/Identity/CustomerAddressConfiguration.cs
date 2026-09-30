using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Identity;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Identity;

/// `customer_address` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> e)
    {
        e.ToTable("customer_address", t =>
        {
            t.HasCheckConstraint("customer_address_postal_code_check", "(postal_code ~ '^[0-9]{5}$'::text)");
            t.HasCheckConstraint("customer_address_recipient_mobile_check", "(recipient_mobile ~ '^\\+9665[0-9]{8}$'::text)");
            t.HasCheckConstraint("customer_address_short_address_check", "(short_address ~ '^[A-Z]{4}[0-9]{4}$'::text)");
        });
        e.HasKey(x => x.Id).HasName("customer_address_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.CustomerId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.RecipientName).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.RecipientMobile).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.City).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.District).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.ShortAddress).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Street).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.BuildingNo).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.PostalCode).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.IsDefault).HasColumnType("boolean").IsRequired().HasDefaultValueSql("false").ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.HasIndex(x => x.CustomerId).IsUnique().HasFilter("is_default").HasDatabaseName("ux_address_default");
        e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).HasConstraintName("customer_address_customer_id_fkey").OnDelete(DeleteBehavior.Cascade);
    }
}
