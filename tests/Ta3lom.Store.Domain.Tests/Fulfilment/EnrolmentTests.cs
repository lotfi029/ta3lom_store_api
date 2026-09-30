using Ta3lom.Store.Domain.Entities.Fulfilment;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Fulfilment;

/// Course enrolments (RUL-39, RUL-42, RUL-50, RUL-52).
public sealed class EnrolmentTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Customer = Guid.CreateVersion7();
    private static readonly Guid Course = Guid.CreateVersion7();

    private static Enrolment Purchase(int? accessDays = null) => Enrolment.ForPurchase(Customer, Course, Guid.CreateVersion7(), accessDays, T0);

    private static Enrolment Active(int? accessDays = null)
    {
        var e = Purchase(accessDays);
        e.MarkSynced("lms-1");
        return e;
    }

    [Fact]
    [Rule("RUL-42")]
    public void A_purchase_starts_pending_sync_with_lifetime_access()
    {
        var lineId = Guid.CreateVersion7();

        var e = Enrolment.ForPurchase(Customer, Course, lineId, null, T0);

        e.CustomerId.ShouldBe(Customer);
        e.ProductId.ShouldBe(Course);
        e.Source.ShouldBe(EnrolmentSource.Purchase);
        e.OrderLineId.ShouldBe(lineId);
        e.SponsoredUnitId.ShouldBeNull();
        e.Status.ShouldBe(EnrolmentStatus.PendingSync);
        e.ProgressPct.ShouldBe((short)0);
        e.GrantedAt.ShouldBe(T0);
        e.ExpiresAt.ShouldBeNull();
        e.IsLive.ShouldBeTrue();
        e.CanLaunch(T0).ShouldBeFalse();
        Should.Throw<DomainException>(() => Enrolment.ForPurchase(Customer, Course, Guid.Empty, null, T0)).Code.ShouldBe("guard.empty");
    }

    [Theory]
    [Rule("RUL-42")]
    [InlineData(90, 90)]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    public void Access_days_set_the_expiry(int accessDays, int? expectedDays)
    {
        Purchase(accessDays).ExpiresAt.ShouldBe(expectedDays is null ? null : (DateTimeOffset?)T0.AddDays(expectedDays.Value));
    }

    [Fact]
    [Rule("RUL-52")]
    public void A_sponsored_seat_belongs_to_the_beneficiary_customer()
    {
        var unit = Guid.CreateVersion7();

        var e = Enrolment.ForSponsoredUnit(Customer, Course, unit, 30, T0);

        e.Source.ShouldBe(EnrolmentSource.Sponsored);
        e.SponsoredUnitId.ShouldBe(unit);
        e.OrderLineId.ShouldBeNull();
        e.Status.ShouldBe(EnrolmentStatus.PendingSync);
        e.ExpiresAt.ShouldBe(T0.AddDays(30));
        Enrolment.ForSponsoredUnit(Customer, Course, unit, null, T0).ExpiresAt.ShouldBeNull();
        Should.Throw<DomainException>(() => Enrolment.ForSponsoredUnit(Customer, Course, Guid.Empty, null, T0)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    public void A_manual_enrolment_is_active_at_once()
    {
        var e = new Enrolment(Customer, Course, EnrolmentSource.Manual, T0);

        e.Status.ShouldBe(EnrolmentStatus.Active);
        e.CanLaunch(T0).ShouldBeTrue();
        Should.Throw<DomainException>(() => new Enrolment(Guid.Empty, Course, EnrolmentSource.Manual, T0)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    [Rule("RUL-42")]
    public void The_lms_acceptance_activates_it_idempotently()
    {
        var e = Purchase();

        e.MarkSynced("lms-1");
        e.Status.ShouldBe(EnrolmentStatus.Active);
        e.LmsEnrolmentRef.ShouldBe("lms-1");

        e.MarkSynced("lms-2");
        e.Status.ShouldBe(EnrolmentStatus.Active);
        e.LmsEnrolmentRef.ShouldBe("lms-2");
    }

    [Fact]
    [Rule("RUL-42")]
    public void Launch_is_allowed_until_the_access_expires_and_records_the_first_start()
    {
        var e = Active(accessDays: 10);

        e.CanLaunch(T0.AddDays(10).AddTicks(-1)).ShouldBeTrue();
        e.CanLaunch(T0.AddDays(10)).ShouldBeFalse();

        e.RecordLaunch(T0.AddDays(1));
        e.RecordLaunch(T0.AddDays(2));
        e.StartedAt.ShouldBe(T0.AddDays(1));
    }

    [Fact]
    [Rule("RUL-50")]
    public void Progress_never_goes_down_and_completion_is_reported_once()
    {
        var e = Active();

        e.ApplyProgress(40, T0.AddDays(1), null, null).ShouldBeFalse();
        e.ProgressPct.ShouldBe((short)40);
        e.StartedAt.ShouldBe(T0.AddDays(1));

        e.ApplyProgress(25, T0.AddDays(3), null, "  ").ShouldBeFalse();
        e.ProgressPct.ShouldBe((short)40);
        e.StartedAt.ShouldBe(T0.AddDays(1));
        e.CertificateNo.ShouldBeNull();

        e.ApplyProgress(90, null, T0.AddDays(20), " CERT-1 ").ShouldBeTrue();
        e.Status.ShouldBe(EnrolmentStatus.Completed);
        e.CompletedAt.ShouldBe(T0.AddDays(20));
        e.ProgressPct.ShouldBe((short)100);
        e.CertificateNo.ShouldBe("CERT-1");

        e.ApplyProgress(100, null, T0.AddDays(21), "CERT-2").ShouldBeFalse();
        e.CompletedAt.ShouldBe(T0.AddDays(20));
        e.CertificateNo.ShouldBe("CERT-1");
        e.CanLaunch(T0.AddYears(1)).ShouldBeTrue();
    }

    [Fact]
    public void Progress_is_clamped_to_zero_to_one_hundred()
    {
        var e = Active();

        e.ApplyProgress(150, null, null, null);
        e.ProgressPct.ShouldBe((short)100);
        e.Status.ShouldBe(EnrolmentStatus.Active);
    }

    [Fact]
    public void Completion_without_a_start_uses_the_completion_time()
    {
        var e = Active();

        e.ApplyProgress(100, null, T0.AddDays(5), null).ShouldBeTrue();

        e.StartedAt.ShouldBe(T0.AddDays(5));
    }

    [Fact]
    [Rule("RUL-50")]
    public void Revoked_access_takes_no_progress_and_cannot_launch()
    {
        var pending = Purchase();
        pending.ApplyProgress(50, T0, null, null).ShouldBeFalse();
        pending.ProgressPct.ShouldBe((short)0);

        var e = Active();
        e.Revoke();

        e.Status.ShouldBe(EnrolmentStatus.Revoked);
        e.IsLive.ShouldBeFalse();
        e.CanLaunch(T0).ShouldBeFalse();
        e.ApplyProgress(50, T0, T0, null).ShouldBeFalse();

        e.Revoke();
        e.Status.ShouldBe(EnrolmentStatus.Revoked);
    }

    [Fact]
    [Rule("RUL-50")]
    public void Every_live_state_can_be_revoked()
    {
        var pending = Purchase();
        var completed = Active();
        completed.ApplyProgress(100, T0, T0.AddDays(1), null);

        pending.Revoke();
        completed.Revoke();

        pending.Status.ShouldBe(EnrolmentStatus.Revoked);
        completed.Status.ShouldBe(EnrolmentStatus.Revoked);
    }
}
