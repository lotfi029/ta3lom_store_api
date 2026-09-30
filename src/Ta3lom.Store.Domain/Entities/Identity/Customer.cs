namespace Ta3lom.Store.Domain.Entities.Identity;

/// Table `customer`. Created on first OTP login (RUL-61) or at allocation for a beneficiary (RUL-52).
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

    public bool IsBlocked => Status == CustomerStatus.Blocked;

    /// The profile is complete when checkout has what it needs (RUL-25): a name and an email.
    public bool ProfileComplete => !string.IsNullOrWhiteSpace(FullName) && !string.IsNullOrWhiteSpace(Email);

    /// First login with a verified mobile (RUL-61). <paramref name="mobileE164"/> is `+9665XXXXXXXX`.
    public static Customer RegisterOnFirstLogin(string mobileE164, DateTimeOffset now) => new(mobileE164, now);

    public void RecordLogin(DateTimeOffset now)
    {
        Guard.Against(Status != CustomerStatus.Active, "customer.not_active");
        LastLoginAt = now;
        UpdatedAt = now;
    }

    public void UpdateProfile(string? fullName, string? email, string? locale, bool? anonymousDefault, bool? marketingOptIn, DateTimeOffset now)
    {
        if (fullName is not null)
        {
            FullName = fullName.Trim();
        }

        if (email is not null)
        {
            Email = email.Trim();
        }

        if (locale is not null)
        {
            Locale = locale is "ar" or "en" ? locale : throw new DomainException("customer.locale_invalid");
        }

        AnonymousDefault = anonymousDefault ?? AnonymousDefault;
        MarketingOptIn = marketingOptIn ?? MarketingOptIn;
        UpdatedAt = now;
    }

    public void RecordPdplConsent(DateTimeOffset now)
    {
        PdplConsentAt ??= now;
        UpdatedAt = now;
    }

    /// Staff action (FR-S79, RUL-61): a blocked customer cannot sign in or check out.
    public void Block(DateTimeOffset now)
    {
        Guard.Against(Status == CustomerStatus.Anonymised, "customer.anonymised");
        Status = CustomerStatus.Blocked;
        UpdatedAt = now;
    }

    public void Unblock(DateTimeOffset now)
    {
        Guard.Against(Status == CustomerStatus.Anonymised, "customer.anonymised");
        Status = CustomerStatus.Active;
        UpdatedAt = now;
    }

    public bool IsAnonymised => Status == CustomerStatus.Anonymised;

    /// PDPL erasure (database/03 §7, S-31): personal fields are cleared, financial rows keep the id. The mobile becomes a
    /// random `+9665` placeholder (the DDL CHECK needs the E.164 shape; `status = anonymised` marks it), so the login is freed.
    public void Anonymise(DateTimeOffset now)
    {
        Status = CustomerStatus.Anonymised;
        FullName = null;
        Email = null;
        Mobile = RandomPlaceholderMobile();
        MarketingOptIn = false;
        AnonymousDefault = false;
        UpdatedAt = now;
    }

    /// A real person signed in with the placeholder an anonymised account happened to receive: move the placeholder away.
    public void ReleaseMobile(DateTimeOffset now)
    {
        Guard.Against(!IsAnonymised, "customer.not_anonymised");
        Mobile = RandomPlaceholderMobile();
        UpdatedAt = now;
    }

    private static string RandomPlaceholderMobile() =>
        "+9665" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
}
