using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Identity;

/// Staff accounts (RUL-62): roles, mandatory TOTP and the FR-53 lockout.
public sealed class StaffUserTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private static StaffUser New(params string[] roles) =>
        StaffUser.Create(" finance@ta3lom.sa ", " سلمى ", "hash", roles.Length == 0 ? [Roles.Finance] : roles, T0);

    [Fact]
    [Rule("RUL-62")]
    public void Create_trims_and_keeps_distinct_roles()
    {
        var staff = New(Roles.Finance, Roles.AllocationOfficer, Roles.Finance);

        staff.Email.ShouldBe("finance@ta3lom.sa");
        staff.Name.ShouldBe("سلمى");
        staff.PassHash.ShouldBe("hash");
        staff.Roles.ShouldBe([Roles.Finance, Roles.AllocationOfficer]);
        staff.IsActive.ShouldBeTrue();
        staff.FailedCount.ShouldBe(0);
        staff.LockedUntil.ShouldBeNull();
        staff.LastLoginAt.ShouldBeNull();
        staff.CreatedAt.ShouldBe(T0);
        staff.TotpEnrolled.ShouldBeFalse();
        staff.TotpSecretEnc.ShouldBeNull();
    }

    [Fact]
    [Rule("RUL-62")]
    public void The_constructor_starts_as_a_viewer()
    {
        var staff = new StaffUser("v@ta3lom.sa", "مشاهد", "hash", T0);

        staff.Roles.ShouldBe([Roles.Viewer]);
        Should.Throw<DomainException>(() => new StaffUser(null!, "n", "h", T0)).Code.ShouldBe("guard.null");
    }

    [Fact]
    [Rule("RUL-62")]
    public void A_user_can_hold_several_roles_and_admin_does_not_imply_the_others()
    {
        var admin = New(Roles.Admin);
        var both = New(Roles.Admin, Roles.Finance);

        admin.HasRole(Roles.Admin).ShouldBeTrue();
        admin.HasRole(Roles.Finance).ShouldBeFalse();
        admin.HasRole(Roles.AllocationOfficer).ShouldBeFalse();
        both.HasRole(Roles.Finance).ShouldBeTrue();
        both.HasRole("ADMIN").ShouldBeFalse();
    }

    [Fact]
    [Rule("RUL-62")]
    public void Roles_are_required_and_must_be_known()
    {
        Should.Throw<DomainException>(() => StaffUser.Create("a@b", "n", "h", [], T0)).Code.ShouldBe("staff.roles_required");
        Should.Throw<DomainException>(() => StaffUser.Create("a@b", "n", "h", ["superuser"], T0)).Code.ShouldBe("staff.role_invalid");
    }

    [Fact]
    [Rule("RUL-62")]
    public void The_fifth_consecutive_failure_locks_the_account_for_one_hour()
    {
        var staff = New();
        for (var i = 1; i < StaffUser.MaxFailures; i++)
        {
            staff.RecordFailure(T0);
            staff.FailedCount.ShouldBe(i);
            staff.IsLocked(T0).ShouldBeFalse();
        }

        staff.RecordFailure(T0);

        staff.LockedUntil.ShouldBe(T0 + StaffUser.LockoutDuration);
        staff.FailedCount.ShouldBe(0);
        staff.IsLocked(T0.AddMinutes(59)).ShouldBeTrue();
        staff.IsLocked(T0.AddHours(1)).ShouldBeFalse();
    }

    [Fact]
    [Rule("RUL-62")]
    public void A_successful_login_resets_the_failures()
    {
        var staff = New();
        staff.RecordFailure(T0);
        staff.RecordFailure(T0);

        staff.RecordLogin(T0.AddMinutes(1));

        staff.FailedCount.ShouldBe(0);
        staff.LockedUntil.ShouldBeNull();
        staff.LastLoginAt.ShouldBe(T0.AddMinutes(1));
    }

    [Fact]
    public void An_admin_unlock_clears_the_lock()
    {
        var staff = New();
        for (var i = 0; i < StaffUser.MaxFailures; i++)
        {
            staff.RecordFailure(T0);
        }

        staff.Unlock();

        staff.IsLocked(T0).ShouldBeFalse();
        staff.FailedCount.ShouldBe(0);
    }

    [Fact]
    [Rule("RUL-62")]
    public void Totp_enrolment_needs_a_secret_and_can_be_reset()
    {
        var staff = New();

        Should.Throw<DomainException>(() => staff.EnrolTotp([])).Code.ShouldBe("staff.totp_secret_required");
        Should.Throw<DomainException>(() => staff.EnrolTotp(null!)).Code.ShouldBe("staff.totp_secret_required");

        staff.EnrolTotp([1, 2, 3]);
        staff.TotpEnrolled.ShouldBeTrue();
        staff.TotpSecretEnc.ShouldBe(new byte[] { 1, 2, 3 });

        staff.ResetTotp();
        staff.TotpEnrolled.ShouldBeFalse();
    }

    [Fact]
    public void Password_change_and_staff_update()
    {
        var staff = New();

        staff.ChangePasswordHash("new-hash");
        staff.Update("  سلمى أحمد ", [Roles.Viewer], isActive: false);

        staff.PassHash.ShouldBe("new-hash");
        staff.Name.ShouldBe("سلمى أحمد");
        staff.Roles.ShouldBe([Roles.Viewer]);
        staff.IsActive.ShouldBeFalse();
        Should.Throw<DomainException>(() => staff.Update(" ", [Roles.Viewer], true)).Code.ShouldBe("guard.empty");
        Should.Throw<DomainException>(() => staff.ChangePasswordHash(null!)).Code.ShouldBe("guard.null");
    }
}
