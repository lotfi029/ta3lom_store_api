using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Donations;

/// `cart` (BR-A07). The partial unique indexes `ux_cart_customer` (one cart per donor) and `ux_cart_option_line` (option
/// lines merge, D-04) are created by raw SQL in the migration.
internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> e)
    {
        e.ToTable("cart");
        e.HasKey(x => x.Id).HasName("pk_cart");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.CreatedAt).IsRequired();
        e.Property(x => x.UpdatedAt).IsRequired();
        e.HasIndex(x => x.ExpiresAt).HasFilter("customer_id IS NULL").HasDatabaseName("ix_cart_guest_expiry");
        e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).HasConstraintName("fk_cart_customer").OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.CartId).HasConstraintName("fk_cart_line_cart").OnDelete(DeleteBehavior.Cascade);
        e.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_lines");
    }
}

/// `cart_line` (D-01, D-02): `ck_line_target`, `ck_line_amount`.
internal sealed class CartLineConfiguration : IEntityTypeConfiguration<CartLine>
{
    public void Configure(EntityTypeBuilder<CartLine> e)
    {
        e.ToTable("cart_line", t =>
        {
            t.HasCheckConstraint("ck_line_target", LineChecks.Target);
            t.HasCheckConstraint("ck_line_amount", LineChecks.Amount);
            t.HasCheckConstraint("ck_line_kind", LineChecks.Kind);
        });
        e.HasKey(x => x.Id).HasName("pk_cart_line");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.TargetKind).HasColumnType("text").IsRequired();
        e.Property(x => x.Quantity).IsRequired();
        e.Property(x => x.UnitAmount).IsRequired();
        e.Property(x => x.Amount).IsRequired();
        e.HasIndex(x => x.CartId).HasDatabaseName("ix_cart_line_cart");
        e.HasIndex(x => x.ProjectId).HasDatabaseName("ix_cart_line_project");
        e.HasIndex(x => x.CategoryId).HasDatabaseName("ix_cart_line_category");
        e.HasIndex(x => x.OptionId).HasDatabaseName("ix_cart_line_option");
        e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).HasConstraintName("fk_cart_line_project").OnDelete(DeleteBehavior.Cascade);
        e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).HasConstraintName("fk_cart_line_category").OnDelete(DeleteBehavior.Cascade);
        e.HasOne<ProjectOption>().WithMany().HasForeignKey(x => x.OptionId).HasConstraintName("fk_cart_line_option").OnDelete(DeleteBehavior.Cascade);
    }
}

/// The CHECKs shared by `cart_line` and `order_line`.
internal static class LineChecks
{
    public const string Target =
        "(target_kind = 'project' AND project_id IS NOT NULL AND category_id IS NULL) OR " +
        "(target_kind = 'category' AND category_id IS NOT NULL AND project_id IS NULL AND option_id IS NULL)";

    public const string Amount = "amount = unit_amount * quantity AND quantity BETWEEN 1 AND 999 AND unit_amount > 0";

    public const string Kind = "target_kind IN ('project', 'category')";
}
