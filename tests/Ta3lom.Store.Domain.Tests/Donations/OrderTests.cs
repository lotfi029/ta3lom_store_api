using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.Errors;
using Ta3lom.Store.Domain.Events;
using Ta3lom.Store.Domain.Tests.Support;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Donations;

public sealed class OrderTests
{
    private static readonly DonorContact Donor = new("سارة", "sara@example.com", "+966551234567", "ar");

    private static OrderLineDraft ProjectLine(int quantity, decimal unit, Guid? optionId = null) =>
        new(TargetKind.Project, Guid.NewGuid(), null, optionId, "مشروع", "Project", optionId is null ? null : "يوم", null, quantity, new Money(unit));

    private static Order Place(params OrderLineDraft[] lines) =>
        Order.Place("T3-26-000001", Guid.NewGuid(), Donor, lines, new string('a', 64), Make.Now.AddMinutes(60), Make.Now);

    [Theory]
    [Rule("D-09")]
    [InlineData(2026, 1, "T3-26-000001")]
    [InlineData(2027, 123456, "T3-27-123456")]
    [InlineData(2030, 1234567, "T3-30-1234567")]
    public void References_are_T3_year_and_a_six_digit_sequence(int year, long sequence, string expected) =>
        Order.FormatReference(year, sequence).ShouldBe(expected);

    [Fact]
    [Rule("D-09")]
    [Rule("D-02")]
    public void A_new_order_is_pending_with_numbered_lines_and_their_total()
    {
        var order = Place(ProjectLine(3, 4m, Guid.NewGuid()), new OrderLineDraft(TargetKind.Category, null, Guid.NewGuid(), null, "الدعوة", null, null, null, 1, new Money(50m)));

        order.Status.ShouldBe(OrderStatus.PendingPayment);
        order.TotalAmount.ShouldBe(new Money(62m));
        order.LineCount.ShouldBe(2);
        order.Lines.Select(l => (int)l.LineNo).ShouldBe([1, 2]);
        order.Lines[0].Amount.ShouldBe(new Money(12m));
        order.PaymentDueAt.ShouldBe(Make.Now.AddMinutes(60));
        order.DomainEvents.OfType<OrderPlaced>().ShouldHaveSingleItem().TotalAmount.ShouldBe("62.00");
    }

    [Fact]
    [Rule("D-01")]
    public void Order_lines_have_exactly_one_target() =>
        Should.Throw<DomainException>(() => Place(new OrderLineDraft(TargetKind.Category, Guid.NewGuid(), Guid.NewGuid(), null, "x", null, null, null, 1, new Money(10m))));

    [Fact]
    public void An_order_needs_lines_and_a_future_due_time()
    {
        Should.Throw<DomainException>(() => Place());
        Should.Throw<DomainException>(() => Order.Place("T3-26-000002", Guid.NewGuid(), Donor, [ProjectLine(1, 10m)], new string('a', 64), Make.Now, Make.Now));
    }

    [Fact]
    [Rule("D-09")]
    public void Staff_can_cancel_only_a_pending_order()
    {
        var order = Place(ProjectLine(1, 10m));

        order.Cancel("finance@ta3lom.sa", Make.Now).IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.CancelledBy.ShouldBe("finance@ta3lom.sa");
        order.DomainEvents.OfType<OrderCancelled>().ShouldHaveSingleItem();

        var again = order.Cancel("admin@ta3lom.sa", Make.Now);
        again.Error.Code.ShouldBe(ErrorCodes.OrderCancelNotAllowed);
        again.Error.Extensions!["status"].ShouldBe("cancelled");
    }

    [Fact]
    [Rule("D-14")]
    public void Anonymising_keeps_the_order_and_its_amounts()
    {
        var order = Place(ProjectLine(1, 10m));

        order.AnonymiseDonor(Make.Now);

        (order.DonorName, order.DonorEmail, order.DonorMobile).ShouldBe((Order.AnonymisedName, Order.AnonymisedEmail, Order.AnonymisedMobile));
        order.TotalAmount.ShouldBe(new Money(10m));
    }
}
