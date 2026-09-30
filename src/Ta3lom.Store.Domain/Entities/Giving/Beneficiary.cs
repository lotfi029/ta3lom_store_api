namespace Ta3lom.Store.Domain.Entities.Giving;

/// Table `beneficiary` (RUL-51, PDPL): `FullName`, `Mobile` and `GuardianName` are plaintext only in memory — the converter
/// stores Data Protection ciphertext — and are never logged; lookups use `MobileHash`. For a minor, `Mobile` is the
/// guardian's (RUL-47e). Every change is audited through the staff actor.
public sealed class Beneficiary : AggregateRoot
{
    public Beneficiary(string code, BeneficiaryKind kind, string beneficiaryGroup, string region, string fullName, string createdBy, DateTimeOffset now) : base(Guid.CreateVersion7())
    {
        Code = Guard.NotNull(code);
        Kind = kind;
        BeneficiaryGroup = Guard.NotNull(beneficiaryGroup);
        Region = Guard.NotNull(region);
        FullName = Guard.NotNull(fullName);
        EligibilityStatus = EligibilityStatus.Pending;
        ZakatEligible = false;
        CreatedAt = now;
        CreatedBy = Guard.NotNull(createdBy);
    }

    private Beneficiary()
    {
    }

    public string Code { get; private set; } = null!;

    public BeneficiaryKind Kind { get; private set; }

    public string BeneficiaryGroup { get; private set; } = null!;

    public string Region { get; private set; } = null!;

    public string FullName { get; private set; } = null!;

    public string? Mobile { get; private set; }

    public string? MobileHash { get; private set; }

    public string? GuardianName { get; private set; }

    public string? OrganisationName { get; private set; }

    public EligibilityStatus EligibilityStatus { get; private set; }

    public bool ZakatEligible { get; private set; }

    public string? EligibilityNotes { get; private set; }

    public string? VerifiedBy { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public Guid? CustomerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string CreatedBy { get; private set; } = null!;

    /// RUL-51: a new beneficiary is `pending`; approval is a separate audited step.
    public static Beneficiary Register(string code, BeneficiaryDetails details, string createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        var b = new Beneficiary(code, details.Kind, details.BeneficiaryGroup, details.Region, details.FullName, createdBy, now);
        b.Apply(details);
        return b;
    }

    public void Update(BeneficiaryDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        Kind = details.Kind;
        BeneficiaryGroup = Guard.NotNull(details.BeneficiaryGroup);
        Region = Guard.NotNull(details.Region);
        FullName = Guard.NotNull(details.FullName);
        Apply(details);
    }

    /// RUL-51: approve, reject or deactivate, with the verifier and time (audited).
    public void SetEligibility(EligibilityStatus status, bool zakatEligible, string? notes, string verifiedBy, DateTimeOffset now)
    {
        if (status == EligibilityStatus.Pending)
        {
            throw new DomainException("beneficiary.status", "Eligibility can be set to approved, rejected or inactive.");
        }

        EligibilityStatus = status;
        ZakatEligible = zakatEligible;
        EligibilityNotes = string.IsNullOrWhiteSpace(notes) ? EligibilityNotes : notes.Trim();
        VerifiedBy = Guard.NotNull(verifiedBy);
        VerifiedAt = now;
    }

    /// Retention (database/03 §7): personal data erased two years after the beneficiary stopped being active; the code,
    /// kind, group, region and history stay. `full_name_enc` is NOT NULL, so the name becomes a fixed placeholder.
    public const string ErasedName = "محذوف";

    public bool PiiErased => FullName == ErasedName && Mobile is null && GuardianName is null;

    public void ErasePii()
    {
        FullName = ErasedName;
        Mobile = null;
        MobileHash = null;
        GuardianName = null;
    }

    /// RUL-52: set at the first course-seat allocation.
    public void LinkCustomer(Guid customerId) => CustomerId ??= Guard.NotEmpty(customerId);

    private void Apply(BeneficiaryDetails d)
    {
        Mobile = d.MobileE164;
        MobileHash = d.MobileHash;
        GuardianName = d.GuardianName;
        OrganisationName = d.OrganisationName;
        EligibilityNotes = d.EligibilityNotes;
    }
}

/// Validated register input (mobile already normalised to E.164 and hashed by the caller).
public sealed record BeneficiaryDetails(BeneficiaryKind Kind, string BeneficiaryGroup, string Region, string FullName, string? MobileE164,
    string? MobileHash, string? GuardianName, string? OrganisationName, string? EligibilityNotes);
