using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Ordering;

/// Dedication card delivery state (RUL-22, #93 resend).
public sealed class DedicationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private static Dedication New() => new(Guid.CreateVersion7(), "سارة", "+966559876543", "eid", "tok");

    [Fact]
    public void A_new_dedication_is_pending()
    {
        var d = New();

        d.SmsStatus.ShouldBe(DedicationSmsStatus.Pending);
        d.ToName.ShouldBe("سارة");
        d.ToMobile.ShouldBe("+966559876543");
        d.TemplateKey.ShouldBe("eid");
        d.CardToken.ShouldBe("tok");
        Should.Throw<DomainException>(() => new Dedication(Guid.Empty, "a", "b", "c", "d"));
    }

    [Fact]
    [Rule("RUL-22")]
    public void Sent_records_the_time_and_failed_keeps_it_empty()
    {
        var sent = New();
        var failed = New();

        sent.MarkSent(T0);
        failed.MarkFailed();

        sent.SmsStatus.ShouldBe(DedicationSmsStatus.Sent);
        sent.SentAt.ShouldBe(T0);
        failed.SmsStatus.ShouldBe(DedicationSmsStatus.Failed);
        failed.SentAt.ShouldBeNull();
    }

    [Fact]
    public void A_staff_resend_goes_back_to_pending_optionally_to_a_corrected_mobile()
    {
        var same = New();
        var corrected = New();
        same.MarkFailed();
        corrected.MarkSent(T0);

        same.PrepareResend(null);
        corrected.PrepareResend("+966551112223");

        same.SmsStatus.ShouldBe(DedicationSmsStatus.Pending);
        same.ToMobile.ShouldBe("+966559876543");
        corrected.SmsStatus.ShouldBe(DedicationSmsStatus.Pending);
        corrected.ToMobile.ShouldBe("+966551112223");
    }
}
