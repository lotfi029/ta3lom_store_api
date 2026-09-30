namespace Ta3lom.Store.Application.DTOs.Beneficiaries;

/// 11 §9.13 row (list, masked): never a plaintext name or mobile.
public sealed record BeneficiaryRow(Guid Id, string Code, string Kind, string BeneficiaryGroup, string Region, string? OrganisationName,
    string NameMasked, string? MobileMasked, string EligibilityStatus, bool ZakatEligible, bool LinkedCustomer, string? VerifiedBy,
    DateTimeOffset? VerifiedAt);

/// Detail (masked) with the units history (RUL-50 statuses; no donor data).
public sealed record BeneficiaryDetail(Guid Id, string Code, string Kind, string BeneficiaryGroup, string Region, string? OrganisationName,
    string NameMasked, string? MobileMasked, bool HasGuardian, string EligibilityStatus, bool ZakatEligible, string? EligibilityNotes,
    bool LinkedCustomer, string? VerifiedBy, DateTimeOffset? VerifiedAt, DateTimeOffset CreatedAt, string CreatedBy,
    IReadOnlyList<BeneficiaryUnit> Units);

public sealed record BeneficiaryUnit(Guid UnitId, string ProductSlug, string TitleAr, string Status, DateTimeOffset? AllocatedAt,
    DateTimeOffset? CompletedAt, DateTimeOffset? LapsedAt);

/// `BeneficiaryInput` (11 §9.13). For a minor, `mobile` is the guardian's (RUL-47e).
public sealed record BeneficiaryInput(string? Kind, string? BeneficiaryGroup, string? Region, string? FullName, string? Mobile,
    string? GuardianName, string? OrganisationName, string? EligibilityNotes);

/// `POST …/eligibility`: `status` = approved | rejected | inactive.
public sealed record EligibilityInput(string? Status, bool ZakatEligible, string? Notes);

public sealed record RevealRequest(string? Reason);

/// `POST …/reveal` (#105): audited as `beneficiary.reveal` with the reason.
public sealed record BeneficiaryPii(string Code, string FullName, string? Mobile, string? GuardianName, DateTimeOffset RevealedAt);

/// CSV import report (#107).
public sealed record ImportReport(bool DryRun, int Rows, int Valid, int Created, IReadOnlyList<ImportRowError> Errors);

public sealed record ImportRowError(int Row, string Code, string Field, string? Message);
