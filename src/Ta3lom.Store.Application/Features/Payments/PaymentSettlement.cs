using System.Text.Json;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Payments;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Payments;

/// SettleOrder (ADR-S05, plan §3.3, review 04): applies a payment RE-FETCHED from the gateway, in one transaction with the
/// order row locked, so a webhook and the reconciliation job settle an order exactly once.
/// Captured with nothing refunded, amount == total in SAR, order pending → `paid`, DR number (gapless), receipt row,
/// outbox OrderPaid + ReceiptIssued; the order's other open attempts are cancelled (their pages too, best effort).
/// Captured otherwise (amount or currency ≠, order expired/cancelled/past due, already paid, another payment in review,
/// part already refunded on the gateway) → `payment_review` (the order, or only the payment on a paid order) + outbox
/// PaymentReviewRequired (finance alert). A refund the gateway shows on a payment is never settled as paid (A-1): it is
/// imported through <see cref="RefundBook"/> (A-2) — on a paid order that is all that happens (no review).
/// Voided after we took it as captured → review `voided_after_capture` (A-1). Failed / voided without a capture → the
/// attempt fails; the order stays `pending_payment` (retry allowed). Anything else → no change.
public sealed class PaymentSettlement(IOrderRepository orders, IPaymentRepository payments, IReceiptRepository receipts,
    IDocumentNumbers numbers, IOutbox outbox, IPaymentGatewayRegistry gateways, RefundBook refundBook, IUnitOfWork uow, IClock clock)
{
    public const string Settled = "settled";
    public const string Review = "review";
    public const string AlreadySettled = "already_settled";
    public const string FailedOutcome = "failed";
    public const string Pending = "pending";
    public const string UnknownPayment = "unknown_payment";
    public const string RefundImported = "refund_imported";

    /// Returns the outcome and our payment id (null when no attempt matches the gateway's payment).
    public async Task<(string Outcome, Guid? PaymentId)> ApplyAsync(string provider, GatewayPayment gp, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gp);
        var cancel = new List<string>();
        var outcome = await uow.ExecuteInTransactionAsync(async t =>
        {
            cancel.Clear();
            var payment = await payments.FindByProviderPaymentIdAsync(provider, gp.ProviderPaymentId, t)
                          ?? (gp.ProviderInvoiceId is { } invoice ? await payments.FindByProviderInvoiceIdAsync(provider, invoice, t) : null);
            if (payment is null)
            {
                return Result.Success<(string, Guid?)>((UnknownPayment, null));
            }

            var order = await orders.GetForUpdateAsync(payment.OrderId, t);          // the lock serialises settlements
            await payments.ReloadAsync(payment, t);
            var attempts = await payments.ForOrderAsync(order!.Id, t);
            if (payment.IsCaptured && payment.ProviderPaymentId is { } known && known != gp.ProviderPaymentId
                && gp.Status is GatewayPaymentStatus.Captured or GatewayPaymentStatus.Refunded && gp.CapturedHalalas > 0)
            {
                // A second payment on the same hosted page (two tabs, a double tap): its own attempt row, then review.
                if (attempts.FirstOrDefault(p => p.ProviderPaymentId == gp.ProviderPaymentId) is { } seen)
                {
                    payment = seen;
                }
                else
                {
                    var extra = Payment.Start(order.Id, attempts.Max(p => p.AttemptNo) + 1, provider, Money.FromHalalas(gp.AmountHalalas), clock.UtcNow);
                    extra.AttachHostedPage(payment.ProviderInvoiceId!, payment.RedirectUrl ?? "about:blank", payment.RedirectExpiresAt, clock.UtcNow);
                    payments.Add(extra);
                    payment = extra;
                    attempts = [.. attempts, extra];
                }
            }

            return Result.Success<(string, Guid?)>((await ApplyLockedAsync(order, attempts, payment, gp, cancel, t), payment.Id));
        }, ct);

        if (cancel.Count > 0 && gateways.Find(provider) is { } gateway)
        {
            foreach (var invoice in cancel)
            {
                await gateway.CancelHostedPaymentAsync(invoice, ct);                // best effort; the page also expires
            }
        }

        return outcome.Value;
    }

    private async Task<string> ApplyLockedAsync(Order order, IReadOnlyList<Payment> attempts, Payment payment, GatewayPayment gp,
        List<string> cancel, CancellationToken ct)
    {
        var now = clock.UtcNow;
        switch (gp.Status)
        {
            case GatewayPaymentStatus.Captured or GatewayPaymentStatus.Refunded when gp.CapturedHalalas > 0:
                if (payment.IsCaptured)
                {
                    // A-2: a refund made outside the store (or one of ours whose answer was lost) shows on the payment.
                    return await refundBook.SyncAsync(order, attempts, payment, gp.RefundedHalalas, null, ct) ? RefundImported : AlreadySettled;
                }

                payment.MarkCaptured(gp.ProviderPaymentId, Money.FromHalalas(gp.CapturedHalalas), gp.Method, now);
                // S-2: no currency is not SAR.
                var exact = gp.CapturedHalalas == order.TotalAmount.ToHalalas() && string.Equals(gp.Currency, Money.Currency, StringComparison.OrdinalIgnoreCase);
                if (exact && gp.RefundedHalalas == 0 && order.Status == OrderStatus.PendingPayment && now < order.PaymentDueAt.AddHours(24))
                {
                    // A capture before expiry was taken while the page was open: it settles even if it is reported late.
                    order.MarkPaid(now).IsSuccess.ShouldHold();
                    var receipt = await IssueDonationReceiptAsync(order, now, ct);
                    outbox.Enqueue(new OrderPaid(order.Id, order.Reference, order.TotalAmount.ToWire(), receipt.Id));
                    outbox.Enqueue(new ReceiptIssued(receipt.Id, order.Id, receipt.DisplayNumber));
                    foreach (var other in attempts.Where(p => p.Id != payment.Id && p.IsOpen))
                    {
                        other.Cancel(now);
                        if (other.ProviderInvoiceId is { } page && page != payment.ProviderInvoiceId)
                        {
                            cancel.Add(page);
                        }
                    }

                    return Settled;
                }

                var reason = order.Status switch
                {
                    OrderStatus.Paid or OrderStatus.Refunded or OrderStatus.PaymentReview => PaymentReviewReason.DoublePayment,
                    OrderStatus.Expired or OrderStatus.Cancelled => PaymentReviewReason.LateCapture,
                    _ when gp.RefundedHalalas > 0 => PaymentReviewReason.ExternalRefund,
                    _ when !exact => PaymentReviewReason.AmountMismatch,
                    _ => PaymentReviewReason.LateCapture,
                };
                payment.MarkForReview(reason, now);
                order.FlagForReview(reason, now);
                outbox.Enqueue(new PaymentReviewRequired(order.Id, order.Reference, payment.Id, reason.ToWire(), payment.CapturedAmount.ToWire()));
                if (gp.RefundedHalalas > 0)
                {
                    await refundBook.SyncAsync(order, attempts, payment, gp.RefundedHalalas, null, ct);
                }

                return Review;
            case GatewayPaymentStatus.Voided when payment.IsCaptured:
                if (payment.ReviewReason == PaymentReviewReason.VoidedAfterCapture || payment.Status == PaymentStatus.Refunded)
                {
                    return AlreadySettled;
                }

                // A-1: the money we took as captured is gone; finance decides (manual resolution, DECISIONS REVIEW).
                payment.MarkVoidedAfterCapture(now);
                order.ReopenForReview(PaymentReviewReason.VoidedAfterCapture, now);
                outbox.Enqueue(new PaymentReviewRequired(order.Id, order.Reference, payment.Id, PaymentReviewReason.VoidedAfterCapture.ToWire(),
                    payment.CapturedAmount.ToWire()));
                return Review;
            case GatewayPaymentStatus.Failed or GatewayPaymentStatus.Voided:
                payment.MarkFailed(gp.ProviderPaymentId, gp.FailureCode, gp.FailureMessage, now);
                return payment.Status == PaymentStatus.Failed ? FailedOutcome : AlreadySettled;
            default:
                return Pending;
        }
    }

    /// DR for a paid order (also used when finance accepts a reviewed payment). Gapless: the number is taken in this transaction.
    public async Task<DonationReceipt> IssueDonationReceiptAsync(Order order, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        var year = clock.ToRiyadh(now).Year;
        var number = await numbers.NextAsync(ReceiptSeries.Dr, year, ct);
        var receipt = DonationReceipt.IssueDonation(order.Id, year, number, order.TotalAmount, order.DonorName, LinesSnapshot(order), now);
        receipts.Add(receipt);
        return receipt;
    }

    public static string LinesSnapshot(Order order) =>
        JsonSerializer.Serialize(order.Lines.OrderBy(l => l.LineNo).Select(l => new ReceiptLine(l.TitleAr, l.TitleEn, l.OptionLabelAr, l.OptionLabelEn,
            l.Quantity, l.UnitAmount.ToWire(), l.Amount.ToWire())), ReceiptLine.Json);
}

/// One line of a receipt snapshot (`donation_receipt.lines_snapshot`).
public sealed record ReceiptLine(string TitleAr, string? TitleEn, string? OptionLabelAr, string? OptionLabelEn, int Quantity, string UnitAmount, string Amount)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

internal static class ResultAssertions
{
    /// A transition the caller has just checked; a failure here is a bug.
    public static void ShouldHold(this bool ok)
    {
        if (!ok)
        {
            throw new InvalidOperationException("A checked transition failed.");
        }
    }
}
