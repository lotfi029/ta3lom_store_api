using Ta3lom.Store.Domain.Entities.Fulfilment;
using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.Events;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Fulfilment;

/// Print shipments: `preparing → shipped → delivered`, or `returned` (RUL-40, FR-S42).
public sealed class ShipmentTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrderId = Guid.CreateVersion7();
    private static readonly ShippingAddress Address = new("نورة", "+966551234567", "الرياض", "العليا", "RAAA1234", "التحلية", "1", "12211");

    private static Shipment New() => new(OrderId, Address);

    [Fact]
    [Rule("RUL-40")]
    public void A_shipment_starts_preparing()
    {
        var s = New();

        s.OrderId.ShouldBe(OrderId);
        s.Status.ShouldBe(ShipmentStatus.Preparing);
        s.Address.ShouldBeSameAs(Address);
        s.Carrier.ShouldBeNull();
        s.TrackingNo.ShouldBeNull();
        s.ShippedAt.ShouldBeNull();
        s.DeliveredAt.ShouldBeNull();
        s.UpdatedBy.ShouldBeNull();
        Should.Throw<DomainException>(() => new Shipment(OrderId, null!)).Code.ShouldBe("guard.null");
        Should.Throw<DomainException>(() => new Shipment(Guid.Empty, Address)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    [Rule("RUL-40")]
    public void Ship_then_deliver_raises_the_customer_messages()
    {
        var s = New();

        s.Ship(" SMSA ", " 123456 ", "fulfilment@ta3lom.sa", T0).ShouldBeTrue();
        s.Status.ShouldBe(ShipmentStatus.Shipped);
        s.Carrier.ShouldBe("SMSA");
        s.TrackingNo.ShouldBe("123456");
        s.ShippedAt.ShouldBe(T0);
        s.UpdatedBy.ShouldBe("fulfilment@ta3lom.sa");

        s.Deliver(T0.AddDays(2), "ops@ta3lom.sa").ShouldBeTrue();
        s.Status.ShouldBe(ShipmentStatus.Delivered);
        s.DeliveredAt.ShouldBe(T0.AddDays(2));
        s.UpdatedBy.ShouldBe("ops@ta3lom.sa");

        s.DomainEvents.ShouldBe([new ShipmentShipped(s.Id, OrderId), new ShipmentDelivered(s.Id, OrderId)]);
    }

    [Fact]
    [Rule("RUL-40")]
    public void Transitions_out_of_order_are_refused()
    {
        var s = New();

        s.Deliver(T0, "a").ShouldBeFalse();
        s.Return("a").ShouldBeFalse();
        s.Status.ShouldBe(ShipmentStatus.Preparing);

        s.Ship("SMSA", "1", "a", T0).ShouldBeTrue();
        s.Ship("Aramex", "2", "a", T0).ShouldBeFalse();
        s.Carrier.ShouldBe("SMSA");

        s.Deliver(T0, "a").ShouldBeTrue();
        s.Deliver(T0.AddDays(1), "a").ShouldBeFalse();
        s.DeliveredAt.ShouldBe(T0);
        s.DomainEvents.Count.ShouldBe(2);
    }

    [Theory]
    [Rule("RUL-40")]
    [InlineData(false)]
    [InlineData(true)]
    public void A_shipped_or_delivered_parcel_can_come_back(bool delivered)
    {
        var s = New();
        s.Ship("SMSA", "1", "a", T0);
        if (delivered)
        {
            s.Deliver(T0.AddDays(1), "a");
        }

        s.Return("returns@ta3lom.sa").ShouldBeTrue();

        s.Status.ShouldBe(ShipmentStatus.Returned);
        s.UpdatedBy.ShouldBe("returns@ta3lom.sa");
        s.Return("again").ShouldBeFalse();
        s.Ship("SMSA", "2", "a", T0).ShouldBeFalse();
    }

    [Fact]
    [Rule("RUL-61")]
    public void Anonymising_replaces_the_recipient_and_keeps_the_city()
    {
        var s = New();

        s.AnonymiseRecipient();

        s.Address.RecipientName.ShouldBe(Order.DeletedContact);
        s.Address.RecipientMobile.ShouldBe(Order.DeletedContact);
        s.Address.City.ShouldBe("الرياض");
        s.Address.Street.ShouldBeNull();
        s.Address.PostalCode.ShouldBeNull();
    }
}
