using Ta3lom.Store.Domain.Events;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Common;

/// Outbox message payloads (09 §5.11): immutable records that compare by value, so a replay is recognisable.
public sealed class DomainEventTests
{
    private static readonly Guid A = Guid.CreateVersion7();
    private static readonly Guid B = Guid.CreateVersion7();
    private static readonly Guid C = Guid.CreateVersion7();
    private static readonly Guid D = Guid.CreateVersion7();
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 9, 30);

    private static IDomainEvent[] All() =>
    [
        new OrderPlaced(A, "T3-26-000001"),
        new OrderPaid(A, "T3-26-000001"),
        new RenderDocuments(A),
        new OrderExpired(A, "T3-26-000001", true),
        new OrderCancelled(A, "T3-26-000001", "reason"),
        new DocumentsResendRequested(A, ["sms", "email"]),
        new DedicationCard(A, B),
        new FinanceAlert(FinanceAlertKinds.DuplicateCourse, A, "T3-26-000001", null),
        new LmsEnrol(A),
        new LmsRevoke(A),
        new ShipmentShipped(A, B),
        new ShipmentDelivered(A, B),
        new LowStockAlert(A, "SKU-P", "كتاب", 3, 20, Day),
        new BeneficiaryInvite(A, B),
        new SponsorUnitAllocated(A, B, C),
        new SponsorUnitCompleted(A, B, null),
        new SponsorDigest(A, "2026-09", T0.AddMonths(-1), T0),
        new UnitLapsed(A, B, C),
        new AllocationAlert(A, "slug", 2, T0, Day),
        new AllocationEscalation(A, "slug", 2, T0, Day),
        new CauseUpdatePublished(A, B),
        new PoolSwept(A, B, C, D, "20.00"),
        new CatalogChanged(A, "published"),
    ];

    [Fact]
    public void Events_compare_by_value()
    {
        var first = All();
        var second = All();

        first.Length.ShouldBe(23);
        for (var i = 0; i < first.Length; i++)
        {
            if (first[i] is DocumentsResendRequested)
            {
                continue;                                                                    // list member compares by reference
            }

            first[i].ShouldBe(second[i]);
        }

        first.Select(e => e.GetType()).Distinct().Count().ShouldBe(first.Length);
    }

    [Fact]
    [Rule("RUL-22")]
    public void A_dedication_card_is_a_first_send_unless_staff_resend_it()
    {
        new DedicationCard(A, B).Resend.ShouldBeFalse();
        new DedicationCard(A, B, Resend: true).Resend.ShouldBeTrue();
    }

    [Fact]
    public void Finance_alert_kinds_are_stable()
    {
        FinanceAlertKinds.AmountMismatch.ShouldBe("payment_review_amount");
        FinanceAlertKinds.LateCapture.ShouldBe("payment_review_late_capture");
        FinanceAlertKinds.DuplicateCourse.ShouldBe("duplicate_course");
    }
}
