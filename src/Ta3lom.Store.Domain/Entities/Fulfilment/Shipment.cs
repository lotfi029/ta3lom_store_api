namespace Ta3lom.Store.Domain.Entities.Fulfilment;

/// Table `shipment` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class Shipment : AggregateRoot
{
    public Shipment(Guid orderId, string address) : base(Guid.CreateVersion7())
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

    public string Address { get; private set; } = null!;

    public DateTimeOffset? ShippedAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public string? UpdatedBy { get; private set; }
}
