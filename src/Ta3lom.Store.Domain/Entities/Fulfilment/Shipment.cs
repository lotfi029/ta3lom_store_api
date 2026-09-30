using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Domain.Entities.Fulfilment;

/// Table `shipment` (RUL-40, FR-S42): one per order with print lines; `preparing → shipped → delivered`, or `returned`.
public sealed class Shipment : AggregateRoot
{
    public Shipment(Guid orderId, ShippingAddress address) : base(Guid.CreateVersion7())
    {
        OrderId = Guard.NotEmpty(orderId);
        Status = ShipmentStatus.Preparing;
        Address = Guard.NotNull(address);
    }

    private Shipment()
    {
    }

    public Guid OrderId { get; private set; }

    public ShipmentStatus Status { get; private set; }

    public string? Carrier { get; private set; }

    public string? TrackingNo { get; private set; }

    /// Owned JSON snapshot of the order address (`shipment.address` jsonb; owner fix 2).
    public ShippingAddress Address { get; private set; } = null!;

    public DateTimeOffset? ShippedAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public string? UpdatedBy { get; private set; }

    public bool Ship(string carrier, string trackingNo, string actor, DateTimeOffset now)
    {
        if (Status != ShipmentStatus.Preparing)
        {
            return false;
        }

        Status = ShipmentStatus.Shipped;
        Carrier = carrier.Trim();
        TrackingNo = trackingNo.Trim();
        ShippedAt = now;
        UpdatedBy = actor;
        Raise(new ShipmentShipped(Id, OrderId));
        return true;
    }

    public bool Deliver(DateTimeOffset deliveredAt, string actor)
    {
        if (Status != ShipmentStatus.Shipped)
        {
            return false;
        }

        Status = ShipmentStatus.Delivered;
        DeliveredAt = deliveredAt;
        UpdatedBy = actor;
        Raise(new ShipmentDelivered(Id, OrderId));
        return true;
    }

    /// Back to the association (refused, damaged, not collected) — from shipped or delivered.
    public bool Return(string actor)
    {
        if (Status is not (ShipmentStatus.Shipped or ShipmentStatus.Delivered))
        {
            return false;
        }

        Status = ShipmentStatus.Returned;
        UpdatedBy = actor;
        return true;
    }

    /// PDPL erasure of the ordering account (database/03 §7): recipient replaced, city kept.
    public void AnonymiseRecipient() =>
        Address = new ShippingAddress(Ordering.Order.DeletedContact, Ordering.Order.DeletedContact, Address.City, null, null, null, null, null);
}
