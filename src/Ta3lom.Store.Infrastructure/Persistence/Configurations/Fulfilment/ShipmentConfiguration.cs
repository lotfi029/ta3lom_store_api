using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ta3lom.Store.Domain.Entities.Fulfilment;

namespace Ta3lom.Store.Infrastructure.Persistence.Configurations.Fulfilment;

/// `shipment` — names, types, defaults and CHECKs match db/reference/001_store_schema.sql (SchemaParityTests).
internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> e)
    {
        e.ToTable("shipment", t =>
        {
            t.HasCheckConstraint("shipment_status_check", "(status = ANY (ARRAY['preparing'::text, 'shipped'::text, 'delivered'::text, 'returned'::text]))");
        });
        e.HasKey(x => x.Id).HasName("shipment_pkey");
        e.Property(x => x.Id).HasColumnType("uuid").IsRequired().HasDefaultValueSql("uuidv7()").ValueGeneratedNever();
        e.Property(x => x.OrderId).HasColumnType("uuid").IsRequired().ValueGeneratedNever();
        e.Property(x => x.Status).HasColumnType("text").IsRequired().HasDefaultValueSql("'preparing'::text").ValueGeneratedNever();
        e.Property(x => x.Carrier).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.TrackingNo).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.OwnsOne(x => x.Address, a =>                           // owned JSON since S-21 (owner fix 2); column stays jsonb NOT NULL
        {
            a.ToJson("address");
            a.Property(x => x.RecipientName).HasJsonPropertyName("recipientName");
            a.Property(x => x.RecipientMobile).HasJsonPropertyName("recipientMobile");
            a.Property(x => x.City).HasJsonPropertyName("city");
            a.Property(x => x.District).HasJsonPropertyName("district");
            a.Property(x => x.ShortAddress).HasJsonPropertyName("shortAddress");
            a.Property(x => x.Street).HasJsonPropertyName("street");
            a.Property(x => x.BuildingNo).HasJsonPropertyName("buildingNo");
            a.Property(x => x.PostalCode).HasJsonPropertyName("postalCode");
        });
        e.Navigation(x => x.Address).IsRequired();
        e.Property(x => x.ShippedAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.DeliveredAt).HasColumnType("timestamp with time zone").IsRequired(false).ValueGeneratedNever();
        e.Property(x => x.UpdatedBy).HasColumnType("text").IsRequired(false).ValueGeneratedNever();
        e.HasIndex(x => x.OrderId).IsUnique().HasDatabaseName("shipment_order_id_key");
        e.HasOne<Ta3lom.Store.Domain.Entities.Ordering.Order>().WithMany().HasForeignKey(x => x.OrderId).HasConstraintName("shipment_order_id_fkey").OnDelete(DeleteBehavior.NoAction);
    }
}
