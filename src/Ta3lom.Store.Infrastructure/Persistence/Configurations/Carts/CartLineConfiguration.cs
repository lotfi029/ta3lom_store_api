using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Carts;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Carts;

/// `cart_line` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class CartLineConfiguration : IEntityTypeConfiguration<CartLine>
{
    public void Configure(EntityTypeBuilder<CartLine> e)
    {
        e.ToTable("cart_line", t =>
        {
            t.HasCheckConstraint("cart_line_amount_check", "(amount > (0)::numeric)");
            t.HasCheckConstraint("cart_line_intent_check", "(intent = ANY (ARRAY['sadaqa'::text, 'zakat'::text]))");
            t.HasCheckConstraint("cart_line_kind_check", "(kind = ANY (ARRAY['buy'::text, 'sponsor'::text, 'donate'::text]))");
            t.HasCheckConstraint("cart_line_mode_check", "(mode = ANY (ARRAY['units'::text, 'amount'::text]))");
            t.HasCheckConstraint("cart_line_quantity_check", "((quantity >= 1) AND (quantity <= 999))");
            t.HasCheckConstraint("cart_line_unit_price_check", "(unit_price >= (0)::numeric)");
            t.HasCheckConstraint("ck_cart_amount_qty", "((mode <> 'amount'::text) OR (quantity = 1))");
            t.HasCheckConstraint("ck_cart_buy_nointent", "((kind <> 'buy'::text) OR ((intent IS NULL) AND (dedication IS NULL)))");
            t.HasCheckConstraint("ck_cart_buy_units", "((kind <> 'buy'::text) OR (mode = 'units'::text))");
        });
        e.HasKey(x => x.Id).HasName("cart_line_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.CartId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.ProductId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.BookFormatId).HasColumnType("uuid").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.Kind).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Mode).HasColumnType("text").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Quantity).HasColumnType("integer").IsRequired().HasDefaultValueSql("1").ValueGeneratedNever();
        e.Property(x => x.UnitPrice).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Amount).HasColumnType("numeric(12,2)").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Intent).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.OwnsOne(x => x.Dedication, d =>                        // RUL-21; owned JSON since S-13 (column stays jsonb)
        {
            d.ToJson("dedication");
            d.Property(x => x.ToName).HasJsonPropertyName("toName");
            d.Property(x => x.ToMobile).HasJsonPropertyName("toMobile");
            d.Property(x => x.FromName).HasJsonPropertyName("fromName");
            d.Property(x => x.TemplateKey).HasJsonPropertyName("templateKey");
            d.Property(x => x.Text).HasJsonPropertyName("text");
        });
        e.Ignore(x => x.IsGift);
        e.Property(x => x.AddedAt).HasColumnType("timestamp with time zone").IsRequired().HasDefaultValueSql("now()").ValueGeneratedNever();
        e.HasIndex(x => x.CartId).HasDatabaseName("ix_cart_line_cart");
        e.HasOne<Ta3lom.Store.Domain.Entities.Catalog.BookFormat>().WithMany().HasForeignKey(x => x.BookFormatId).HasConstraintName("cart_line_book_format_id_fkey").OnDelete(DeleteBehavior.NoAction);
        e.HasOne<Ta3lom.Store.Domain.Entities.Catalog.Product>().WithMany().HasForeignKey(x => x.ProductId).HasConstraintName("cart_line_product_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
