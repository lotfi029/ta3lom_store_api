using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Payments;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.Errors;
using Ta3lom.Store.Domain.Events;
using Ta3lom.Store.Domain.Tests.Support;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.SharedKernel;

namespace Ta3lom.Store.Domain.Tests.Payments;

/// ADR-S05 / F-D4 / F-D5: the order and payment state machines, refunds and receipts.
public sealed class PaymentStateTests
{
    private static readonly DonorContact Donor = new("سارة", "sara@example.com", "+966551234567", "ar");

    private static Order Pending(decimal total = 100m) =>
        Order.Place("T3-26-000001", Guid.NewGuid(), Donor,
            [new OrderLineDraft(TargetKind.Project, Guid.NewGuid(), null, null, "مشروع", null, null, null, 1, new Money(total))],
            new string('a', 64), Make.Now.AddMinutes(60), Make.Now);

    private static Payment Captured(Order order, decimal amount)
    {
        var payment = Payment.Start(order.Id, 1, "fake", order.TotalAmount, Make.Now);
        payment.MarkCaptured("pay_1", new Money(amount), PaymentMethod.Mada, Make.Now);
        return payment;
    }

    [Fact]
    public void A_pending_order_settles_once()
    {
        var order = Pending();
        order.IsPayable(Make.Now).ShouldBeTrue();
        order.MarkPaid(Make.Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Paid);
        order.PaidAt.ShouldBe(Make.Now.AddMinutes(1));
        order.IsPayable(Make.Now).ShouldBeFalse();
        var again = order.MarkPaid(Make.Now);
        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe(ErrorCodes.PaymentNotPayable);
    }

    [Theory]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.PaymentReview, true)]
    [InlineData(OrderStatus.Expired, OrderStatus.PaymentReview, true)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.PaymentReview, true)]
    [InlineData(OrderStatus.Paid, OrderStatus.Paid, false)]
    public void A_capture_that_cannot_settle_goes_to_review_except_on_a_paid_order(OrderStatus from, OrderStatus expected, bool changed)
    {
        var order = Pending();
        switch (from)
        {
            case OrderStatus.Expired:
                order.Expire(Make.Now.AddHours(2)).IsSuccess.ShouldBeTrue();
                break;
            case OrderStatus.Cancelled:
                order.Cancel("staff@ta3lom.sa", Make.Now).IsSuccess.ShouldBeTrue();
                break;
            case OrderStatus.Paid:
                order.MarkPaid(Make.Now).IsSuccess.ShouldBeTrue();
                break;
        }

        order.FlagForReview(PaymentReviewReason.LateCapture, Make.Now).ShouldBe(changed);
        order.Status.ShouldBe(expected);
        if (changed)
        {
            order.ReviewReason.ShouldBe(PaymentReviewReason.LateCapture);
            order.AcceptReview(Make.Now).IsSuccess.ShouldBeTrue();
            order.Status.ShouldBe(OrderStatus.Paid);
        }
        else
        {
            order.AcceptReview(Make.Now).Error.Code.ShouldBe(ErrorCodes.PaymentNotInReview);
        }
    }

    [Fact]
    public void Only_an_overdue_pending_order_expires()
    {
        var order = Pending();
        order.Expire(Make.Now.AddMinutes(30)).IsFailure.ShouldBeTrue();
        order.Expire(Make.Now.AddMinutes(60)).IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Expired);
        order.ExpiredAt.ShouldBe(Make.Now.AddMinutes(60));
        order.DomainEvents.OfType<OrderExpired>().ShouldHaveSingleItem();
        order.Expire(Make.Now.AddHours(3)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Partial_then_full_refund_moves_a_paid_order_to_refunded()
    {
        var order = Pending(100m);
        order.MarkPaid(Make.Now);
        order.ApplyRefund(new Money(30m), new Money(100m), Make.Now).IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Paid);
        order.RefundedAmount.ShouldBe(new Money(30m));
        order.ApplyRefund(new Money(80m), new Money(100m), Make.Now).Error.Code.ShouldBe(ErrorCodes.PaymentRefundExceeds);
        order.ApplyRefund(new Money(70m), new Money(100m), Make.Now.AddDays(1)).IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Refunded);
        order.RefundedAt.ShouldBe(Make.Now.AddDays(1));
        order.ApplyRefund(new Money(1m), new Money(100m), Make.Now).Error.Code.ShouldBe(ErrorCodes.PaymentRefundNotAllowed);
    }

    [Fact]
    public void A_pending_order_cannot_be_refunded_and_a_refund_must_be_positive()
    {
        var order = Pending();
        order.ApplyRefund(new Money(1m), new Money(100m), Make.Now).Error.Code.ShouldBe(ErrorCodes.PaymentRefundNotAllowed);
        order.MarkPaid(Make.Now);
        Should.Throw<DomainException>(() => order.ApplyRefund(Money.Zero, new Money(100m), Make.Now));
    }

    [Fact]
    public void A_payment_moves_initiated_to_captured_and_ignores_a_late_failure()
    {
        var order = Pending();
        var payment = Payment.Start(order.Id, 1, "moyasar", order.TotalAmount, Make.Now);
        payment.IsOpen.ShouldBeTrue();
        payment.AttachHostedPage("inv_1", "https://checkout.moyasar.com/invoices/inv_1", Make.Now.AddHours(1), Make.Now);
        payment.ProviderInvoiceId.ShouldBe("inv_1");
        payment.MarkCaptured("pay_1", new Money(100m), PaymentMethod.Mada, Make.Now);
        payment.Status.ShouldBe(PaymentStatus.Captured);
        payment.Refundable.ShouldBe(new Money(100m));
        payment.MarkCaptured("pay_1", new Money(100m), PaymentMethod.Mada, Make.Now);   // idempotent
        payment.MarkFailed("pay_1", "05", "DECLINED", Make.Now);                         // out of order → ignored
        payment.Status.ShouldBe(PaymentStatus.Captured);
        Should.Throw<DomainException>(() => payment.AttachHostedPage("inv_2", "https://x", null, Make.Now));
    }

    [Fact]
    public void A_failed_attempt_keeps_a_short_message_and_cancel_only_touches_an_open_attempt()
    {
        var order = Pending();
        var payment = Payment.Start(order.Id, 1, "fake", order.TotalAmount, Make.Now);
        payment.MarkFailed("pay_x", "51", new string('x', 300), Make.Now);
        payment.Status.ShouldBe(PaymentStatus.Failed);
        payment.FailureMessage!.Length.ShouldBe(200);
        payment.Cancel(Make.Now);
        payment.Status.ShouldBe(PaymentStatus.Failed);
        var open = Payment.Start(order.Id, 2, "fake", order.TotalAmount, Make.Now);
        open.Cancel(Make.Now);
        open.Status.ShouldBe(PaymentStatus.Cancelled);
        Should.Throw<DomainException>(() => Payment.Start(order.Id, 0, "fake", order.TotalAmount, Make.Now));
        Should.Throw<DomainException>(() => Payment.Start(order.Id, 1, "fake", Money.Zero, Make.Now));
    }

    [Fact]
    public void Review_and_refunds_on_the_payment()
    {
        var order = Pending();
        var payment = Captured(order, 120m);
        payment.MarkForReview(PaymentReviewReason.AmountMismatch, Make.Now);
        payment.Status.ShouldBe(PaymentStatus.Review);
        payment.ReviewReason.ShouldBe(PaymentReviewReason.AmountMismatch);
        payment.AcceptReview(Make.Now);
        payment.Status.ShouldBe(PaymentStatus.Captured);
        payment.ApplyRefund(new Money(20m), Make.Now);
        payment.Refundable.ShouldBe(new Money(100m));
        Should.Throw<DomainException>(() => payment.ApplyRefund(new Money(101m), Make.Now));
        payment.ApplyRefund(new Money(100m), Make.Now);
        payment.Status.ShouldBe(PaymentStatus.Refunded);
        payment.Refundable.ShouldBe(Money.Zero);
        var open = Payment.Start(order.Id, 2, "fake", order.TotalAmount, Make.Now);
        Should.Throw<DomainException>(() => open.MarkForReview(PaymentReviewReason.DoublePayment, Make.Now));
    }

    [Fact]
    public void Refund_requests_succeed_or_fail_once()
    {
        var refund = Refund.Request(Guid.NewGuid(), Guid.NewGuid(), new Money(10m), "  طلب المتبرع  ", "finance@ta3lom.sa", "key-12345", Make.Now);
        refund.Status.ShouldBe(RefundStatus.Requested);
        refund.Reason.ShouldBe("طلب المتبرع");
        refund.Succeed("rf_1", Make.Now);
        refund.Status.ShouldBe(RefundStatus.Succeeded);
        Should.Throw<DomainException>(() => refund.Fail("x", Make.Now));
        var failed = Refund.Request(Guid.NewGuid(), Guid.NewGuid(), new Money(10m), "r", "f@x.sa", "key-12345", Make.Now);
        failed.Fail("DECLINED", Make.Now);
        failed.Status.ShouldBe(RefundStatus.Failed);
        Should.Throw<DomainException>(() => Refund.Request(Guid.NewGuid(), Guid.NewGuid(), Money.Zero, "r", "f@x.sa", "k", Make.Now));
    }

    [Fact]
    public void A_cancellation_receipt_voids_the_donation_receipt_once()
    {
        var order = Pending();
        var dr = DonationReceipt.IssueDonation(order.Id, 2026, 7, order.TotalAmount, "سارة", "[]", Make.Now);
        dr.DisplayNumber.ShouldBe("DR-2026-000007");
        var drc = DonationReceipt.IssueCancellation(dr, 2026, 1, order.TotalAmount, Make.Now);
        drc.DisplayNumber.ShouldBe("DRC-2026-000001");
        drc.VoidsReceiptId.ShouldBe(dr.Id);
        dr.VoidedByReceiptId.ShouldBe(drc.Id);
        Should.Throw<DomainException>(() => DonationReceipt.IssueCancellation(dr, 2026, 2, order.TotalAmount, Make.Now));
        Should.Throw<DomainException>(() => DonationReceipt.IssueCancellation(drc, 2026, 2, order.TotalAmount, Make.Now));
        Should.Throw<DomainException>(() => DonationReceipt.IssueDonation(order.Id, 2026, 0, order.TotalAmount, "x", "[]", Make.Now));
        dr.StorePdf("receipts/2026/DR-2026-000007.pdf");
        dr.PdfStorageKey.ShouldNotBeNull();
        dr.AnonymiseDonor();
        dr.DonorNameSnapshot.ShouldBe(Order.AnonymisedName);
    }

    [Fact]
    public void Webhook_events_record_their_outcome()
    {
        var e = PaymentEvent.Receive("moyasar", "evt_1", "payment_paid", "pay_1", true, "{}", Make.Now);
        e.Type.ShouldBe("payment_paid");
        var paymentId = Guid.NewGuid();
        e.Processed(paymentId, "settled", Make.Now);
        e.PaymentId.ShouldBe(paymentId);
        e.Outcome.ShouldBe("settled");
        PaymentEvent.Receive("moyasar", "evt_2", "", null, false, "{}", Make.Now).Type.ShouldBe("unknown");
    }
}
