using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.Errors;
using Ta3lom.Store.Domain.Services;
using Ta3lom.Store.Domain.ValueObjects;

namespace Ta3lom.Store.Domain.Tests.Giving;

/// T-4 (plan 14 S-25 "one test per RUL-47 (a)–(e)"): the allocation checks of `AllocationPolicy`.
public sealed class AllocationPolicyTests
{
    private const string Group = "rural_private_school_teachers";
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private static Beneficiary Person(bool approved = true, bool zakat = false, string group = Group, string? mobile = "+966551998301")
    {
        var b = Beneficiary.Register("B-000001",
            new BeneficiaryDetails(BeneficiaryKind.Teacher, group, "القصيم", "معلمة تجربة", mobile, mobile is null ? null : "hash", null, "مدرسة", null),
            "admin@test", Now);
        if (approved)
        {
            b.SetEligibility(EligibilityStatus.Approved, zakat, null, "admin@test", Now);
        }

        return b;
    }

    private static SponsoredUnit Unit(bool zakat = false) =>
        SponsoredUnit.Full(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), new Money(350m), zakat, Now);

    [Fact]
    [Rule("RUL-47")]
    public void A_eligible_beneficiary_passes_every_check()
    {
        var b = Person();

        AllocationPolicy.CheckPerson(b, isCourse: true, hasLiveEnrolment: false).ShouldBeNull();
        AllocationPolicy.CheckPair(b, Group, Unit()).ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-47")]
    public void a_only_approved_beneficiaries() =>
        AllocationPolicy.CheckPerson(Person(approved: false), isCourse: true, hasLiveEnrolment: false).ShouldBe(ErrorCodes.AllocationNotApproved);

    [Fact]
    [Rule("RUL-47")]
    public void b_the_group_must_match_the_offer() =>
        AllocationPolicy.CheckPair(Person(group: "orphan_home_students"), Group, Unit()).ShouldBe(ErrorCodes.AllocationGroupMismatch);

    [Fact]
    [Rule("RUL-47")]
    public void c_no_second_seat_in_a_course_already_held() =>
        AllocationPolicy.CheckPerson(Person(), isCourse: true, hasLiveEnrolment: true).ShouldBe(ErrorCodes.AllocationAlreadyEnrolled);

    [Fact]
    [Rule("RUL-47")]
    [Rule("RUL-20")]
    public void d_zakat_funded_units_only_for_zakat_eligible_beneficiaries()
    {
        AllocationPolicy.CheckPair(Person(zakat: false), Group, Unit(zakat: true)).ShouldBe(ErrorCodes.AllocationZakatMismatch);
        AllocationPolicy.CheckPair(Person(zakat: true), Group, Unit(zakat: true)).ShouldBeNull();
        AllocationPolicy.CheckPair(Person(zakat: true), Group, Unit(zakat: false)).ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-47")]
    public void e_a_course_seat_needs_a_mobile_a_book_copy_does_not()
    {
        AllocationPolicy.CheckPerson(Person(mobile: null), isCourse: true, hasLiveEnrolment: false).ShouldBe(ErrorCodes.AllocationMobileRequired);
        AllocationPolicy.CheckPerson(Person(mobile: null), isCourse: false, hasLiveEnrolment: false).ShouldBeNull();
    }
}
