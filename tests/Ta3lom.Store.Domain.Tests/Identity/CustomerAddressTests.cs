using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Identity;

/// Saved National Addresses (FR-S22, RUL-26).
public sealed class CustomerAddressTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private static readonly ShippingAddress Home = new("نورة العتيبي", "+966551234567", "الرياض", "العليا", "RAAA1234", "شارع التحلية", "1234", "12211");

    [Fact]
    public void Create_copies_every_field_and_is_not_default()
    {
        var customerId = Guid.CreateVersion7();

        var a = CustomerAddress.Create(customerId, Home, T0);

        a.CustomerId.ShouldBe(customerId);
        a.RecipientName.ShouldBe("نورة العتيبي");
        a.RecipientMobile.ShouldBe("+966551234567");
        a.City.ShouldBe("الرياض");
        a.District.ShouldBe("العليا");
        a.ShortAddress.ShouldBe("RAAA1234");
        a.Street.ShouldBe("شارع التحلية");
        a.BuildingNo.ShouldBe("1234");
        a.PostalCode.ShouldBe("12211");
        a.IsDefault.ShouldBeFalse();
        a.CreatedAt.ShouldBe(T0);
    }

    [Fact]
    public void Round_trips_to_a_shipping_address()
    {
        var a = CustomerAddress.Create(Guid.CreateVersion7(), Home, T0);

        var s = a.ToShippingAddress();

        s.ShouldNotBeSameAs(Home);
        s.RecipientName.ShouldBe(Home.RecipientName);
        s.RecipientMobile.ShouldBe(Home.RecipientMobile);
        s.City.ShouldBe(Home.City);
        s.District.ShouldBe(Home.District);
        s.ShortAddress.ShouldBe(Home.ShortAddress);
        s.Street.ShouldBe(Home.Street);
        s.BuildingNo.ShouldBe(Home.BuildingNo);
        s.PostalCode.ShouldBe(Home.PostalCode);
    }

    [Fact]
    public void Update_replaces_every_field()
    {
        var a = CustomerAddress.Create(Guid.CreateVersion7(), Home, T0);

        a.Update(new ShippingAddress("خالد", "+966559876543", "جدة", null, null, null, null, null));

        a.RecipientName.ShouldBe("خالد");
        a.RecipientMobile.ShouldBe("+966559876543");
        a.City.ShouldBe("جدة");
        a.District.ShouldBeNull();
        a.ShortAddress.ShouldBeNull();
        a.Street.ShouldBeNull();
        a.BuildingNo.ShouldBeNull();
        a.PostalCode.ShouldBeNull();
    }

    [Fact]
    public void Default_flag_can_be_set_and_cleared()
    {
        var a = new CustomerAddress(Guid.CreateVersion7(), "نورة", "+966551234567", "الرياض", T0);

        a.SetDefault(true);
        a.IsDefault.ShouldBeTrue();
        a.SetDefault(false);
        a.IsDefault.ShouldBeFalse();
    }

    [Fact]
    public void Required_parts_are_guarded()
    {
        Should.Throw<DomainException>(() => new CustomerAddress(Guid.Empty, "n", "m", "c", T0)).Code.ShouldBe("guard.empty");
        Should.Throw<DomainException>(() => new CustomerAddress(Guid.CreateVersion7(), "n", "m", null!, T0)).Code.ShouldBe("guard.null");
        Should.Throw<ArgumentNullException>(() => CustomerAddress.Create(Guid.CreateVersion7(), null!, T0));
        var a = CustomerAddress.Create(Guid.CreateVersion7(), Home, T0);
        Should.Throw<ArgumentNullException>(() => a.Update(null!));
        Should.Throw<DomainException>(() => a.Update(new ShippingAddress("n", "m", null!, null, null, null, null, null))).Code.ShouldBe("guard.null");
    }

    [Fact]
    public void Shipping_address_copy_is_a_new_equal_snapshot()
    {
        var copy = Home.Copy();

        copy.ShouldNotBeSameAs(Home);
        copy.RecipientName.ShouldBe(Home.RecipientName);
        copy.RecipientMobile.ShouldBe(Home.RecipientMobile);
        copy.City.ShouldBe(Home.City);
        copy.District.ShouldBe(Home.District);
        copy.ShortAddress.ShouldBe(Home.ShortAddress);
        copy.Street.ShouldBe(Home.Street);
        copy.BuildingNo.ShouldBe(Home.BuildingNo);
        copy.PostalCode.ShouldBe(Home.PostalCode);
    }
}
