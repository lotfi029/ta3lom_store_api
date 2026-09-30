using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Carts;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Carts;

/// `cart` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> e)
    {
        e.ToTable("cart", t =>
        {
            t.HasCheckConstraint("cart_status_check", "(status = ANY (ARRAY['active'::text, 'converted'::text, 'merged'::text, 'expired'::text]))");
        });
        e.HasKey(x => x.Id).HasName("cart_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.CustomerId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Status).HasColumnType("text").IsRequired().HasDefaultValueSql("'active'::text").ValueGeneratedNever();
        e.Property(x => x.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.HasIndex(x => x.CustomerId).IsUnique().HasFilter("((status = 'active'::text) AND (customer_id IS NOT NULL))").HasDatabaseName("ux_cart_customer_active");
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.CartId).HasConstraintName("cart_line_cart_id_fkey").OnDelete(DeleteBehavior.Cascade);
        e.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        e.Ignore(x => x.Total);
        e.Ignore(x => x.IsGuest);
        e.HasOne<Ta3lom.Store.Domain.Entities.Identity.Customer>().WithMany().HasForeignKey(x => x.CustomerId).HasConstraintName("cart_customer_id_fkey").OnDelete(DeleteBehavior.Cascade);
    }
}
