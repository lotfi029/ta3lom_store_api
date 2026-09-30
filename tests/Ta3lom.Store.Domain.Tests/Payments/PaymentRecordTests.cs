using Ta3lom.Store.Domain.Entities.Payments;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Payments;

/// The payment-side tables this backend reads (doc 15: the payment service owns their behaviour).
public sealed class PaymentRecordTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_payment_starts_initiated()
    {
        var order = Guid.CreateVersion7();

        var p = new Payment(order, "moyasar", new Money(350m), T0);

        p.OrderId.ShouldBe(order);
        p.Provider.ShouldBe("moyasar");
        p.Amount.ShouldBe(new Money(350m));
        p.Status.ShouldBe(PaymentStatus.Initiated);
        p.ProviderPaymentId.ShouldBeNull();
        p.Method.ShouldBeNull();
        p.FailureCode.ShouldBeNull();
        p.FailureMessage.ShouldBeNull();
        p.CreatedAt.ShouldBe(T0);
        p.UpdatedAt.ShouldBe(T0);
        Should.Throw<DomainException>(() => new Payment(Guid.Empty, "p", Money.Zero, T0)).Code.ShouldBe("guard.empty");
    }

    [Fact]
    [Rule("RUL-33")]
    public void A_captured_payment_keeps_the_provider_reference_and_method()
    {
        var order = Guid.CreateVersion7();

        var p = Payment.Captured(order, "dev", "pay_123", PaymentMethod.Mada, new Money(350m), T0);

        p.Status.ShouldBe(PaymentStatus.Captured);
        p.ProviderPaymentId.ShouldBe("pay_123");
        p.Method.ShouldBe(PaymentMethod.Mada);
    }

    [Fact]
    [Rule("RUL-34")]
    public void A_payment_event_stores_the_provider_event_id_and_raw_payload()
    {
        var e = new PaymentEvent("moyasar", "evt_1", "payment_paid", true, """{"id":"evt_1"}""", T0);

        e.Provider.ShouldBe("moyasar");
        e.ProviderEventId.ShouldBe("evt_1");
        e.EventType.ShouldBe("payment_paid");
        e.SignatureValid.ShouldBeTrue();
        e.Payload.ShouldBe("""{"id":"evt_1"}""");
        e.ReceivedAt.ShouldBe(T0);
        e.ProcessedAt.ShouldBeNull();
        e.Error.ShouldBeNull();
        Should.Throw<DomainException>(() => new PaymentEvent("p", null!, "t", false, "{}", T0)).Code.ShouldBe("guard.null");
    }

    [Fact]
    [Rule("RUL-56")]
    public void A_refund_is_requested_by_a_named_staff_member()
    {
        var order = Guid.CreateVersion7();
        var payment = Guid.CreateVersion7();

        var r = new Refund(order, payment, new Money(100m), "donor error", "finance@ta3lom.sa", T0);

        r.OrderId.ShouldBe(order);
        r.PaymentId.ShouldBe(payment);
        r.Amount.ShouldBe(new Money(100m));
        r.Reason.ShouldBe("donor error");
        r.Status.ShouldBe(RefundStatus.Requested);
        r.RequestedBy.ShouldBe("finance@ta3lom.sa");
        r.ProviderRefundId.ShouldBeNull();
        r.CreatedAt.ShouldBe(T0);
        r.CompletedAt.ShouldBeNull();
        Should.Throw<DomainException>(() => new Refund(order, Guid.Empty, Money.Zero, "r", "a", T0)).Code.ShouldBe("guard.empty");
        Should.Throw<DomainException>(() => new Refund(order, payment, Money.Zero, "r", null!, T0)).Code.ShouldBe("guard.null");
    }

    [Fact]
    public void A_refund_line_links_a_refund_to_an_order_line()
    {
        var refund = Guid.CreateVersion7();
        var line = Guid.CreateVersion7();

        var rl = new RefundLine(refund, line, new Money(50m));

        rl.RefundId.ShouldBe(refund);
        rl.OrderLineId.ShouldBe(line);
        rl.Amount.ShouldBe(new Money(50m));
        rl.Units.ShouldBeNull();
        Should.Throw<DomainException>(() => new RefundLine(refund, Guid.Empty, Money.Zero)).Code.ShouldBe("guard.empty");
    }
}
