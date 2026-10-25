namespace Ta3lom.Store.Domain.Entities.Identity;

/// Table `customer`: a donor, created on the first OTP sign-in (BR-A09), which also records the PDPL consent (D-14).
public sealed class Customer : AggregateRoot
{
    private Customer(string mobileE164, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        Mobile = mobileE164;
        Locale = "ar";
        Status = CustomerStatus.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    private Customer()
    {
    }

    /// E.164; null once the donor is anonymised (L-5), so the number is free for a new sign-up.
    public string? Mobile { get; private set; }

    public string? FullName { get; private set; }

    public string? Email { get; private set; }

    public string Locale { get; private set; } = null!;

    public bool MarketingOptIn { get; private set; }

    public CustomerStatus Status { get; private set; }

    public DateTimeOffset? PdplConsentAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public bool IsBlocked => Status == CustomerStatus.Blocked;

    public bool IsAnonymised => Status == CustomerStatus.Anonymised;

    /// D-13: only an active donor may sign in, refresh, check out or change their data.
    public bool IsActive => Status == CustomerStatus.Active;

    /// First sign-in with a verified mobile (`+9665XXXXXXXX`): consent to the privacy notice is recorded now (D-14).
    public static Customer RegisterOnFirstLogin(string mobileE164, DateTimeOffset now)
    {
        Guard.Against(!SaudiMobile.TryNormalize(mobileE164, out var normalized) || normalized != mobileE164, "customer.mobile_invalid");
        var customer = new Customer(mobileE164, now);
        customer.PdplConsentAt = now;
        return customer;
    }

    public void RecordLogin(DateTimeOffset now)
    {
        Guard.Against(!IsActive, "customer.not_active");
        PdplConsentAt ??= now;
        LastLoginAt = now;
        UpdatedAt = now;
    }

    public void UpdateProfile(string? fullName, string? email, string? locale, bool? marketingOptIn, DateTimeOffset now)
    {
        Guard.Against(!IsActive, "customer.not_active");
        if (fullName is not null)
        {
            FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
        }

        if (email is not null)
        {
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        }

        if (locale is not null)
        {
            Locale = locale is "ar" or "en" ? locale : throw new DomainException("customer.locale_invalid");
        }

        MarketingOptIn = marketingOptIn ?? MarketingOptIn;
        UpdatedAt = now;
    }

    /// Staff action (D-13): a blocked donor cannot sign in, refresh or check out.
    public void Block(DateTimeOffset now)
    {
        Guard.Against(IsAnonymised, "customer.anonymised");
        Status = CustomerStatus.Blocked;
        UpdatedAt = now;
    }

    public void Unblock(DateTimeOffset now)
    {
        Guard.Against(IsAnonymised, "customer.anonymised");
        Status = CustomerStatus.Active;
        UpdatedAt = now;
    }

    /// PDPL deletion (D-14): personal fields are cleared (the mobile too, which frees the number for a new sign-up); the
    /// orders keep the id.
    public void Anonymise(DateTimeOffset now)
    {
        Status = CustomerStatus.Anonymised;
        FullName = null;
        Email = null;
        Mobile = null;
        MarketingOptIn = false;
        UpdatedAt = now;
    }
}
