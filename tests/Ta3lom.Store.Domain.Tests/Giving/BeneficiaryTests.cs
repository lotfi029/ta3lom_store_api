using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Giving;

/// The beneficiary register (RUL-51, RUL-52, PDPL retention).
public sealed class BeneficiaryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private static BeneficiaryDetails Details(string? mobile = "+966551998301", string? notes = "طالب متفوق") =>
        new(BeneficiaryKind.Student, "rural_students", "القصيم", "محمد", mobile, mobile is null ? null : "hash", "أبو محمد", null, notes);

    private static Beneficiary Register() => Beneficiary.Register("B-000123", Details(), "admin@ta3lom.sa", T0);

    [Fact]
    [Rule("RUL-51")]
    public void A_new_beneficiary_is_pending_and_not_zakat_eligible()
    {
        var b = Register();

        b.Code.ShouldBe("B-000123");
        b.Kind.ShouldBe(BeneficiaryKind.Student);
        b.BeneficiaryGroup.ShouldBe("rural_students");
        b.Region.ShouldBe("القصيم");
        b.FullName.ShouldBe("محمد");
        b.Mobile.ShouldBe("+966551998301");
        b.MobileHash.ShouldBe("hash");
        b.GuardianName.ShouldBe("أبو محمد");
        b.OrganisationName.ShouldBeNull();
        b.EligibilityNotes.ShouldBe("طالب متفوق");
        b.EligibilityStatus.ShouldBe(EligibilityStatus.Pending);
        b.ZakatEligible.ShouldBeFalse();
        b.VerifiedBy.ShouldBeNull();
        b.VerifiedAt.ShouldBeNull();
        b.CustomerId.ShouldBeNull();
        b.CreatedAt.ShouldBe(T0);
        b.CreatedBy.ShouldBe("admin@ta3lom.sa");
        b.PiiErased.ShouldBeFalse();
        Should.Throw<ArgumentNullException>(() => Beneficiary.Register("B-1", null!, "a", T0));
        Should.Throw<DomainException>(() => new Beneficiary(null!, BeneficiaryKind.School, "g", "r", "n", "a", T0)).Code.ShouldBe("guard.null");
    }

    [Fact]
    public void Update_replaces_the_register_details()
    {
        var b = Register();

        b.Update(new BeneficiaryDetails(BeneficiaryKind.School, "rural_schools", "حائل", "مدرسة النور", null, null, null, "مدرسة النور", null));

        b.Kind.ShouldBe(BeneficiaryKind.School);
        b.BeneficiaryGroup.ShouldBe("rural_schools");
        b.Region.ShouldBe("حائل");
        b.FullName.ShouldBe("مدرسة النور");
        b.Mobile.ShouldBeNull();
        b.MobileHash.ShouldBeNull();
        b.GuardianName.ShouldBeNull();
        b.OrganisationName.ShouldBe("مدرسة النور");
        b.EligibilityNotes.ShouldBeNull();
        Should.Throw<ArgumentNullException>(() => b.Update(null!));
    }

    [Fact]
    [Rule("RUL-51")]
    public void Eligibility_records_the_verifier_and_keeps_notes_when_none_are_given()
    {
        var b = Register();

        b.SetEligibility(EligibilityStatus.Approved, true, "  ", "officer@ta3lom.sa", T0.AddDays(1));

        b.EligibilityStatus.ShouldBe(EligibilityStatus.Approved);
        b.ZakatEligible.ShouldBeTrue();
        b.EligibilityNotes.ShouldBe("طالب متفوق");
        b.VerifiedBy.ShouldBe("officer@ta3lom.sa");
        b.VerifiedAt.ShouldBe(T0.AddDays(1));

        b.SetEligibility(EligibilityStatus.Inactive, false, " moved away ", "officer@ta3lom.sa", T0.AddDays(2));
        b.EligibilityStatus.ShouldBe(EligibilityStatus.Inactive);
        b.EligibilityNotes.ShouldBe("moved away");
    }

    [Fact]
    [Rule("RUL-51")]
    public void Eligibility_cannot_go_back_to_pending()
    {
        var b = Register();

        Should.Throw<DomainException>(() => b.SetEligibility(EligibilityStatus.Pending, false, null, "a", T0)).Code.ShouldBe("beneficiary.status");
        Should.Throw<DomainException>(() => b.SetEligibility(EligibilityStatus.Rejected, false, null, null!, T0)).Code.ShouldBe("guard.null");
    }

    [Fact]
    [Rule("RUL-52")]
    public void The_customer_link_is_set_once_at_the_first_seat()
    {
        var b = Register();
        var first = Guid.CreateVersion7();

        b.LinkCustomer(first);
        b.LinkCustomer(Guid.CreateVersion7());

        b.CustomerId.ShouldBe(first);
    }

    [Fact]
    public void Erasing_pii_keeps_the_code_and_history_fields()
    {
        var b = Register();

        b.ErasePii();

        b.PiiErased.ShouldBeTrue();
        b.FullName.ShouldBe(Beneficiary.ErasedName);
        b.Mobile.ShouldBeNull();
        b.MobileHash.ShouldBeNull();
        b.GuardianName.ShouldBeNull();
        b.Code.ShouldBe("B-000123");
        b.Region.ShouldBe("القصيم");
    }

    [Fact]
    public void A_beneficiary_without_contact_details_is_not_erased()
    {
        var b = Beneficiary.Register("B-000124", Details(mobile: null) with { GuardianName = null }, "a", T0);

        b.PiiErased.ShouldBeFalse();
    }
}
