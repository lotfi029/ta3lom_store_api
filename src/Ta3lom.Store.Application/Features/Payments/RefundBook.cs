using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Payments;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Payments;

/// The one place a succeeded refund changes money (review 04 A-2/A-6, P-3): a finance refund, a pending refund the gateway
/// later confirms, and a refund made on the gateway's dashboard all go through <see cref="Apply"/> and
/// <see cref="FinishAsync"/> — payment and order amounts, `refunded` + DRC voiding the DR when nothing is left, outbox
/// `RefundProcessed` for the donor. Callers hold the order row lock.
public sealed class RefundBook(IRefundRepository refunds, IReceiptRepository receipts, IDocumentNumbers numbers, IOutbox outbox, IClock clock)
{
    public const string NotConfirmed = "not confirmed by the gateway";

    /// Applies one succeeded refund of `payment`; returns what it took off the order (zero for an extra payment of a
    /// settled order — a double payment — whose refund leaves the order as it is).
    public static Money Apply(Order order, IReadOnlyList<Payment> attempts, Payment payment, Money amount, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(payment);
        var all = attempts.Any(p => p.Id == payment.Id) ? attempts : [.. attempts, payment];
        var doublePayment = payment.Status == PaymentStatus.Review && order.Status is OrderStatus.Paid or OrderStatus.Refunded;
        payment.ApplyRefund(amount, now);
        if (doublePayment || order.Status is not (OrderStatus.Paid or OrderStatus.PaymentReview))
        {
            return Money.Zero;
        }

        var captured = all.Where(p => p.IsCaptured && (order.Status == OrderStatus.PaymentReview || p.Status != PaymentStatus.Review || p.Id == payment.Id))
            .Select(p => p.CapturedAmount).Sum();
        order.ApplyRefund(amount, captured, now).IsSuccess.ShouldHold();
        return amount;
    }

    /// After the refunds of one transaction: the DRC when the order ended `refunded`, and `RefundProcessed`.
    public async Task<Guid?> FinishAsync(Order order, Guid? lastRefund, Money refundedOnOrder, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        Guid? drcId = null;
        if (order.Status == OrderStatus.Refunded && await receipts.DonationReceiptForOrderAsync(order.Id, ct) is { VoidedByReceiptId: null } dr)
        {
            var year = clock.ToRiyadh(now).Year;
            var drc = DonationReceipt.IssueCancellation(dr, year, await numbers.NextAsync(ReceiptSeries.Drc, year, ct), dr.Amount, now);
            receipts.Add(drc);
            outbox.Enqueue(new ReceiptIssued(drc.Id, order.Id, drc.DisplayNumber));
            drcId = drc.Id;
        }

        if (lastRefund is { } refundId && (refundedOnOrder.IsPositive || drcId is not null))
        {
            outbox.Enqueue(new RefundProcessed(order.Id, order.Reference, refundId, refundedOnOrder.ToWire(), order.Status == OrderStatus.Refunded, drcId));
        }

        return drcId;
    }

    /// Aligns our refunds of `payment` with the refunded amount the gateway reports (re-fetched): requested refunds it
    /// covers succeed, oldest first (P-3); what is left over was made outside the store and is imported as a `gateway`
    /// refund with a staff alert (A-2); requested refunds it does not cover fail once they were requested before
    /// `failBefore` (A-6 — a later appearance is then imported). True when anything changed.
    public async Task<bool> SyncAsync(Order order, IReadOnlyList<Payment> attempts, Payment payment, long gatewayRefundedHalalas,
        DateTimeOffset? failBefore, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(payment);
        if (!payment.IsCaptured)
        {
            return false;
        }

        var now = clock.UtcNow;
        var gap = gatewayRefundedHalalas - payment.RefundedAmount.ToHalalas();
        var changed = false;
        Guid? last = null;
        var onOrder = Money.Zero;
        foreach (var refund in (await refunds.ForOrderAsync(order.Id, ct)).Where(r => r.PaymentId == payment.Id && r.Status == RefundStatus.Requested))
        {
            if (gap >= refund.Amount.ToHalalas() && refund.Amount <= payment.Refundable)
            {
                refund.Succeed(null, now);
                gap -= refund.Amount.ToHalalas();
                onOrder += Apply(order, attempts, payment, refund.Amount, now);
                last = refund.Id;
                changed = true;
            }
            else if (failBefore is { } limit && refund.RequestedAt <= limit)
            {
                refund.Fail(NotConfirmed, now);
                changed = true;
            }
        }

        var external = Math.Min(gap, payment.Refundable.ToHalalas());
        if (external > 0)
        {
            var amount = Money.FromHalalas(external);
            var imported = Refund.Import(order.Id, payment.Id, amount, now);
            refunds.Add(imported);
            onOrder += Apply(order, attempts, payment, amount, now);
            last = imported.Id;
            changed = true;
            outbox.Enqueue(new StaffAlert("external_refund", order.Reference, $"refund made outside the store: {amount.ToWire()} SAR"));
        }

        if (last is not null)
        {
            await FinishAsync(order, last, onOrder, now, ct);
        }

        return changed;
    }
}

/// Re-reads a payment whose refund is still `requested` and settles it through <see cref="RefundBook.SyncAsync"/>: from
/// the `ReconcileRefunds` job (every 5 minutes) and when finance retries with the same Idempotency-Key. The gateway read is
/// outside the transaction; the order row lock serialises it with webhooks and finance actions.
public sealed class RefundReconciliation(IOrderRepository orders, IPaymentRepository payments, IPaymentGatewayRegistry gateways,
    IStoreSettings settings, RefundBook book, IUnitOfWork uow, IClock clock)
{
    /// True when a refund changed; null when the gateway could not be read (try again later).
    public async Task<bool?> ReconcileAsync(Guid paymentId, CancellationToken ct)
    {
        var attempt = await payments.GetByIdAsync(paymentId, ct);
        if (attempt?.ProviderPaymentId is not { } providerPaymentId || gateways.Find(attempt.Provider) is not { } gateway)
        {
            return false;
        }

        GatewayPayment? gp;
        try
        {
            gp = await gateway.GetPaymentAsync(providerPaymentId, ct);
        }
        catch (PaymentGatewayException)
        {
            return null;
        }

        var timeout = TimeSpan.FromMinutes((await settings.GetAsync(ct)).RefundPendingTimeoutMinutes);
        var changed = await uow.ExecuteInTransactionAsync(async t =>
        {
            var order = (await orders.GetForUpdateAsync(attempt.OrderId, t))!;
            var attempts = await payments.ForOrderAsync(order.Id, t);
            var payment = attempts.First(p => p.Id == paymentId);
            await payments.ReloadAsync(payment, t);
            return Result.Success(await book.SyncAsync(order, attempts, payment, gp?.RefundedHalalas ?? 0, clock.UtcNow - timeout, t));
        }, ct);
        return changed.Value;
    }
}
