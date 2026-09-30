using Ta3lom.Store.Domain.Common;

namespace Ta3lom.Store.Domain.Giving;

/// Table `beneficiary` (db/reference/001_store_schema.sql). State only in S-02; behaviour arrives with its session.
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
}
