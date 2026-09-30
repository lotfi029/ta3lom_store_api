namespace Ta3lom.Store.Domain.ValueObjects;

/// Saudi National Address snapshot (`orders.shipping_address`, later `shipment.address`) as an owned JSON type.
/// The recipient mobile is E.164.
public sealed class ShippingAddress
{
    public ShippingAddress(string recipientName, string recipientMobile, string city, string? district, string? shortAddress, string? street,
        string? buildingNo, string? postalCode)
    {
        RecipientName = recipientName;
        RecipientMobile = recipientMobile;
        City = city;
        District = district;
        ShortAddress = shortAddress;
        Street = street;
        BuildingNo = buildingNo;
        PostalCode = postalCode;
    }

    private ShippingAddress()
    {
    }

    public string RecipientName { get; private set; } = null!;

    public string RecipientMobile { get; private set; } = null!;

    public string City { get; private set; } = null!;

    public string? District { get; private set; }

    public string? ShortAddress { get; private set; }

    public string? Street { get; private set; }

    public string? BuildingNo { get; private set; }

    public string? PostalCode { get; private set; }

    public ShippingAddress Copy() => new(RecipientName, RecipientMobile, City, District, ShortAddress, Street, BuildingNo, PostalCode);
}
