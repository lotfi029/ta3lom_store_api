namespace Ta3lom.Store.Domain.Entities.Identity;

/// Table `customer` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
public sealed class Customer : AggregateRoot
{
    public Customer(string mobile, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        Mobile = Guard.NotNull(mobile);
        Locale = "ar";
        AnonymousDefault = false;
        MarketingOptIn = false;
        Status = CustomerStatus.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    private Customer()
    {
    }

    public string Mobile { get; private set; } = null!;

    public string? FullName { get; private set; }

    public string? Email { get; private set; }

    public string Locale { get; private set; } = null!;

    public bool AnonymousDefault { get; private set; }

    public bool MarketingOptIn { get; private set; }

    public CustomerStatus Status { get; private set; }

    public DateTimeOffset? PdplConsentAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
