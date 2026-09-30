namespace Ta3lom.Store.Domain.Entities.Identity;

/// Table `customer_address` (FR-S22, RUL-26): a saved Saudi National Address; at most one default per customer
/// (`ux_address_default`).
public sealed class CustomerAddress : Entity
{
    public CustomerAddress(Guid customerId, string recipientName, string recipientMobile, string city, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        CustomerId = Guard.NotEmpty(customerId);
        RecipientName = Guard.NotNull(recipientName);
        RecipientMobile = Guard.NotNull(recipientMobile);
        City = Guard.NotNull(city);
        IsDefault = false;
        CreatedAt = now;
    }

    private CustomerAddress()
    {
    }

    public Guid CustomerId { get; private set; }

    public string RecipientName { get; private set; } = null!;

    public string RecipientMobile { get; private set; } = null!;

    public string City { get; private set; } = null!;

    public string? District { get; private set; }

    public string? ShortAddress { get; private set; }

    public string? Street { get; private set; }

    public string? BuildingNo { get; private set; }

    public string? PostalCode { get; private set; }

    public bool IsDefault { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// A National Address saved from checkout (`saveAddress`) or `/v1/me/addresses` (S-31).
    public static CustomerAddress Create(Guid customerId, ShippingAddress a, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new CustomerAddress(customerId, a.RecipientName, a.RecipientMobile, a.City, now)
        {
            District = a.District,
            ShortAddress = a.ShortAddress,
            Street = a.Street,
            BuildingNo = a.BuildingNo,
            PostalCode = a.PostalCode,
        };
    }

    public void Update(ShippingAddress a)
    {
        ArgumentNullException.ThrowIfNull(a);
        RecipientName = Guard.NotNull(a.RecipientName);
        RecipientMobile = Guard.NotNull(a.RecipientMobile);
        City = Guard.NotNull(a.City);
        District = a.District;
        ShortAddress = a.ShortAddress;
        Street = a.Street;
        BuildingNo = a.BuildingNo;
        PostalCode = a.PostalCode;
    }

    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    public ShippingAddress ToShippingAddress() => new(RecipientName, RecipientMobile, City, District, ShortAddress, Street, BuildingNo, PostalCode);
}
