using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.Tests.Support;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Identity;

public sealed class CustomerTests
{
    [Fact]
    [Rule("D-14")]
    public void The_first_sign_in_records_the_PDPL_consent()
    {
        var customer = Customer.RegisterOnFirstLogin("+966551234567", Make.Now);

        customer.PdplConsentAt.ShouldBe(Make.Now);
        customer.Status.ShouldBe(CustomerStatus.Active);
        customer.Locale.ShouldBe("ar");
        customer.RecordLogin(Make.Now.AddDays(1));
        customer.PdplConsentAt.ShouldBe(Make.Now);
        customer.LastLoginAt.ShouldBe(Make.Now.AddDays(1));
    }

    [Theory]
    [InlineData("0551234567")]
    [InlineData("+96655123456")]
    public void Only_a_normalised_mobile_registers(string mobile) =>
        Should.Throw<DomainException>(() => Customer.RegisterOnFirstLogin(mobile, Make.Now));

    [Fact]
    [Rule("D-13")]
    public void A_blocked_or_anonymised_donor_cannot_sign_in_or_change_data()
    {
        var blocked = Customer.RegisterOnFirstLogin("+966551234567", Make.Now);
        blocked.Block(Make.Now);
        var deleted = Customer.RegisterOnFirstLogin("+966551234568", Make.Now);
        deleted.Anonymise(Make.Now);

        blocked.IsActive.ShouldBeFalse();
        deleted.IsActive.ShouldBeFalse();
        Should.Throw<DomainException>(() => blocked.RecordLogin(Make.Now));
        Should.Throw<DomainException>(() => deleted.UpdateProfile("x", null, null, null, Make.Now));
        Should.Throw<DomainException>(() => deleted.Block(Make.Now));
        blocked.Unblock(Make.Now);
        blocked.IsActive.ShouldBeTrue();
    }

    [Fact]
    [Rule("D-14")]
    public void Deletion_anonymises_the_personal_data()
    {
        var customer = Customer.RegisterOnFirstLogin("+966551234567", Make.Now);
        customer.UpdateProfile(" سارة ", "sara@example.com", "en", true, Make.Now);

        customer.Anonymise(Make.Now);

        customer.IsAnonymised.ShouldBeTrue();
        customer.FullName.ShouldBeNull();
        customer.Email.ShouldBeNull();
        customer.MarketingOptIn.ShouldBeFalse();
        customer.Mobile.ShouldBeNull();                       // L-5: the number is free for a new sign-up
    }

    [Fact]
    public void Profile_updates_trim_and_validate_the_locale()
    {
        var customer = Customer.RegisterOnFirstLogin("+966551234567", Make.Now);
        customer.UpdateProfile(" سارة ", " sara@example.com ", "en", true, Make.Now);

        (customer.FullName, customer.Email, customer.Locale, customer.MarketingOptIn).ShouldBe(("سارة", "sara@example.com", "en", true));
        customer.UpdateProfile("", "", null, null, Make.Now);
        (customer.FullName, customer.Email).ShouldBe((null, null));
        Should.Throw<DomainException>(() => customer.UpdateProfile(null, null, "fr", null, Make.Now));
    }
}

public sealed class StaffUserTests
{
    private static StaffUser Staff(params string[] roles) => StaffUser.Create(" admin@ta3lom.sa ", " Admin ", "hash", roles.Length == 0 ? [Roles.Admin] : roles, Make.Now);

    [Fact]
    [Rule("D-12")]
    public void Five_failures_within_15_minutes_lock_the_account_for_one_hour()
    {
        var staff = Staff();
        for (var i = 0; i < 4; i++)
        {
            staff.RecordFailure(Make.Now.AddMinutes(i * 3)).ShouldBe(FailureOutcome.Counted);
        }

        staff.RecordFailure(Make.Now.AddMinutes(14)).ShouldBe(FailureOutcome.LockedNow);

        staff.IsLocked(Make.Now.AddMinutes(14)).ShouldBeTrue();
        staff.LockedUntil.ShouldBe(Make.Now.AddMinutes(74));
        staff.IsLocked(Make.Now.AddMinutes(75)).ShouldBeFalse();
    }

    [Fact]
    [Rule("D-12")]
    public void Failures_while_locked_count_without_extending_and_the_window_restarts_after_the_lock()
    {
        var staff = Staff();
        for (var i = 0; i < 5; i++)
        {
            staff.RecordFailure(Make.Now);
        }

        for (var i = 0; i < 3; i++)
        {
            staff.RecordFailure(Make.Now.AddMinutes(10 + i)).ShouldBe(FailureOutcome.WhileLocked);
        }

        staff.FailedCount.ShouldBe(8);
        staff.LockedUntil.ShouldBe(Make.Now.AddHours(1));

        staff.RecordFailure(Make.Now.AddMinutes(61)).ShouldBe(FailureOutcome.Counted);

        staff.IsLocked(Make.Now.AddMinutes(61)).ShouldBeFalse();
        (staff.FailedCount, staff.FirstFailedAt, staff.LockedUntil).ShouldBe((1, (DateTimeOffset?)Make.Now.AddMinutes(61), (DateTimeOffset?)null));
    }

    [Fact]
    [Rule("D-12")]
    public void Failures_outside_the_window_start_a_new_count()
    {
        var staff = Staff();
        for (var i = 0; i < 4; i++)
        {
            staff.RecordFailure(Make.Now);
        }

        staff.RecordFailure(Make.Now.AddMinutes(16)).ShouldBe(FailureOutcome.Counted);

        staff.FailedCount.ShouldBe(1);
        staff.FirstFailedAt.ShouldBe(Make.Now.AddMinutes(16));
    }

    [Fact]
    [Rule("D-12")]
    public void A_TOTP_step_is_never_accepted_twice()
    {
        var staff = Staff();
        staff.EnrolTotp([1, 2, 3]);

        staff.AcceptTotpStep(100).ShouldBeTrue();
        staff.AcceptTotpStep(100).ShouldBeFalse();
        staff.AcceptTotpStep(99).ShouldBeFalse();
        staff.AcceptTotpStep(101).ShouldBeTrue();
        staff.TotpVerified.ShouldBeTrue();
    }

    [Fact]
    public void Re_enrolling_is_allowed_until_the_first_verified_code()
    {
        var staff = Staff();
        staff.EnrolTotp([1]);
        staff.EnrolTotp([2]);
        staff.TotpEnrolled.ShouldBeTrue();
        staff.TotpVerified.ShouldBeFalse();

        staff.AcceptTotpStep(5);

        Should.Throw<DomainException>(() => staff.EnrolTotp([3]));
        staff.ResetTotp();
        staff.TotpEnrolled.ShouldBeFalse();
        staff.EnrolTotp([3]);
    }

    [Fact]
    [Rule("D-12")]
    public void A_new_account_must_change_its_password_and_a_reset_asks_again()
    {
        var staff = Staff();
        staff.MustChangePassword.ShouldBeTrue();

        staff.ChangePassword("new-hash", Make.Now);
        (staff.MustChangePassword, staff.PassHash, staff.PasswordChangedAt).ShouldBe((false, "new-hash", (DateTimeOffset?)Make.Now));

        staff.ResetPassword("otp-hash", Make.Now.AddDays(1));
        staff.MustChangePassword.ShouldBeTrue();
        staff.UpgradePasswordHash("rehash");
        staff.PassHash.ShouldBe("rehash");
    }

    [Fact]
    public void Roles_are_validated_and_a_login_clears_the_failures()
    {
        Should.Throw<DomainException>(() => Staff("allocation_officer"));
        Should.Throw<DomainException>(() => StaffUser.Create("a@b.c", "A", "h", [], Make.Now));
        var staff = Staff(Roles.Finance, Roles.Finance, Roles.Viewer);
        staff.Roles.ShouldBe([Roles.Finance, Roles.Viewer]);
        staff.Email.ShouldBe("admin@ta3lom.sa");
        staff.IsAdmin.ShouldBeFalse();

        staff.RecordFailure(Make.Now);
        staff.RecordLogin(Make.Now);
        (staff.FailedCount, staff.FirstFailedAt, staff.LastLoginAt).ShouldBe((0, null, (DateTimeOffset?)Make.Now));

        staff.Update(" New ", [Roles.Admin], isActive: false);
        (staff.Name, staff.IsAdmin, staff.IsActive).ShouldBe(("New", true, false));
        for (var i = 0; i < 5; i++)
        {
            staff.RecordFailure(Make.Now);
        }

        staff.Unlock();
        staff.IsLocked(Make.Now).ShouldBeFalse();
    }
}

public sealed class RefreshTokenTests
{
    [Fact]
    [Rule("D-11")]
    public void Rotation_keeps_the_family_and_never_extends_its_absolute_expiry()
    {
        var first = RefreshToken.StartFamily(TokenSubjectType.Customer, Guid.NewGuid(), new string('a', 64),
            SessionPolicy.CustomerFamilyLifetime, SessionPolicy.CustomerRefreshLifetime, "ua", Make.Now);

        var second = first.Rotate(new string('b', 64), SessionPolicy.CustomerRefreshLifetime, "ua", Make.Now.AddDays(45));

        second.FamilyId.ShouldBe(first.FamilyId);
        second.FamilyExpiresAt.ShouldBe(Make.Now.AddDays(60));
        second.ExpiresAt.ShouldBe(Make.Now.AddDays(60));                      // capped by the family, not now + 30 days
        first.IsRevoked.ShouldBeTrue();
        first.ReplacedBy.ShouldBe(second.Id);
        second.IsExpired(Make.Now.AddDays(60)).ShouldBeTrue();
        Should.Throw<DomainException>(() => first.Rotate(new string('c', 64), TimeSpan.FromDays(1), null, Make.Now));
    }

    [Fact]
    [Rule("D-11")]
    public void Staff_families_last_eight_hours()
    {
        var token = RefreshToken.StartFamily(TokenSubjectType.Staff, Guid.NewGuid(), new string('a', 64),
            SessionPolicy.StaffFamilyLifetime, SessionPolicy.StaffFamilyLifetime, new string('x', 400), Make.Now);

        token.ExpiresAt.ShouldBe(Make.Now.AddHours(8));
        token.UserAgent!.Length.ShouldBe(300);
        SessionPolicy.CustomerAccessLifetime.ShouldBe(TimeSpan.FromMinutes(15));
        SessionPolicy.StaffAccessLifetime.ShouldBe(TimeSpan.FromMinutes(10));
    }
}

public sealed class OtpChallengeTests
{
    [Fact]
    [Rule("D-10")]
    public void A_code_lives_five_minutes_and_allows_five_attempts()
    {
        var otp = OtpChallenge.Issue(Guid.NewGuid(), "+966551234567", OtpPurpose.Login, new string('a', 64), null,
            Make.Now + SessionPolicy.OtpLifetime, Make.Now);

        otp.IsOpen(Make.Now.AddMinutes(4)).ShouldBeTrue();
        otp.IsOpen(Make.Now.AddMinutes(5)).ShouldBeFalse();
        for (var i = 0; i < 5; i++)
        {
            otp.IsExhausted.ShouldBeFalse();
            otp.RecordFailure();
        }

        otp.IsExhausted.ShouldBeTrue();
        otp.AttemptsLeft.ShouldBe(0);
        otp.Consume(Make.Now);
        otp.IsOpen(Make.Now).ShouldBeFalse();
        Should.Throw<DomainException>(otp.RecordFailure);
        (SessionPolicy.OtpMaxPerMobile, SessionPolicy.OtpMobileWindow, SessionPolicy.OtpMaxPerIp, SessionPolicy.OtpIpWindow)
            .ShouldBe((3, TimeSpan.FromMinutes(15), 10, TimeSpan.FromHours(1)));
    }
}
