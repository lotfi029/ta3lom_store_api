using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Donations;

/// `orders` (BR-A08, D-09). The status CHECK already lists the R5-Payments values, so payments need no enum migration.
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> e)
    {
        e.ToTable("orders", t =>
        {
            t.HasCheckConstraint("ck_orders_status", "status IN ('pending_payment', 'paid', 'payment_review', 'cancelled', 'expired')");
            t.HasCheckConstraint("ck_orders_locale", "locale IN ('ar', 'en')");
            t.HasCheckConstraint("ck_orders_reference", "reference ~ '^T3-[0-9]{2}-[0-9]{6,}$'");
            t.HasCheckConstraint("ck_orders_total", "total_amount > 0 AND line_count > 0");
        });
        e.HasKey(x => x.Id).HasName("pk_orders");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Reference).HasColumnType("text").IsRequired();
        e.Property(x => x.Status).HasColumnType("text").IsRequired();
        e.Property(x => x.DonorName).HasColumnType("text").IsRequired();
        e.Property(x => x.DonorEmail).HasColumnType("citext").IsRequired();
        e.Property(x => x.DonorMobile).HasColumnType("text").IsRequired();
        e.Property(x => x.Locale).HasColumnType("character(2)").IsRequired();
        e.Property(x => x.TotalAmount).IsRequired();
        e.Property(x => x.LineCount).IsRequired();
        e.Property(x => x.StatusTokenHash).HasColumnType("character(64)").IsRequired();
        e.Property(x => x.PaymentDueAt).IsRequired();
        e.Property(x => x.CancelledBy).HasColumnType("text");
        e.Property(x => x.IdempotencyKey).HasColumnType("text").HasMaxLength(64);
        e.Property(x => x.Version).IsRowVersion();
        e.HasIndex(x => x.Reference).IsUnique().HasDatabaseName("ux_orders_reference");
        e.HasIndex(x => new { x.CustomerId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL").HasDatabaseName("ux_orders_idempotency");
        e.HasIndex(x => new { x.CustomerId, x.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_orders_customer");
        e.HasIndex(x => new { x.Status, x.PaymentDueAt }).HasDatabaseName("ix_orders_status");
        e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).HasConstraintName("fk_orders_customer").OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.OrderId).HasConstraintName("fk_order_line_order").OnDelete(DeleteBehavior.Cascade);
        e.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_lines");
    }
}

/// `order_line`: snapshots; the targets are RESTRICT (a project with orders cannot be deleted), the option SET NULL.
internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> e)
    {
        e.ToTable("order_line", t =>
        {
            t.HasCheckConstraint("ck_line_target", LineChecks.Target);
            t.HasCheckConstraint("ck_line_amount", LineChecks.Amount);
            t.HasCheckConstraint("ck_line_kind", LineChecks.Kind);
        });
        e.HasKey(x => x.Id).HasName("pk_order_line");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.LineNo).IsRequired();
        e.Property(x => x.TargetKind).HasColumnType("text").IsRequired();
        e.Property(x => x.TitleAr).HasColumnType("text").IsRequired();
        e.Property(x => x.TitleEn).HasColumnType("text");
        e.Property(x => x.OptionLabelAr).HasColumnType("text");
        e.Property(x => x.OptionLabelEn).HasColumnType("text");
        e.Property(x => x.Quantity).IsRequired();
        e.Property(x => x.UnitAmount).IsRequired();
        e.Property(x => x.Amount).IsRequired();
        e.HasIndex(x => new { x.OrderId, x.LineNo }).IsUnique().HasDatabaseName("ux_order_line_no");
        e.HasIndex(x => x.ProjectId).HasDatabaseName("ix_order_line_project");
        e.HasIndex(x => x.CategoryId).HasDatabaseName("ix_order_line_category");
        e.HasIndex(x => x.OptionId).HasDatabaseName("ix_order_line_option");
        e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).HasConstraintName("fk_order_line_project").OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).HasConstraintName("fk_order_line_category").OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ProjectOption>().WithMany().HasForeignKey(x => x.OptionId).HasConstraintName("fk_order_line_option").OnDelete(DeleteBehavior.SetNull);
    }
}
