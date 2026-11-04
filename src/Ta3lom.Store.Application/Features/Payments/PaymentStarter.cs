using Ta3lom.Store.Application.DTOs.Donations;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Payments;

namespace Ta3lom.Store.Application.Features.Payments;

/// Opens a payment attempt on the gateway's hosted page (ADR-S05, plan §3.3). Three steps, the gateway call OUTSIDE any
/// transaction: (1) under the order lock, decide the attempt (reuse the open one, or create attempt n+1 and cancel the
/// previous open one); (2) call the gateway; (3) store the hosted page on the attempt. A gateway failure leaves the order
/// `pending_payment` with an open attempt without a page — the next call (checkout replay A-1, `/pay`) tries again.
public sealed class PaymentStarter(IOrderRepository orders, IPaymentRepository payments, IPaymentGatewayRegistry gateways,
    IOrderStatusTokens statusTokens, IWebLinks links, IUnitOfWork uow, IClock clock)
{
    /// `newAttempt` = the donor asked to pay again (`/pay`): an open attempt with a page is replaced by a fresh one.
    public async Task<Result<CheckoutPayment>> StartAsync(Guid orderId, bool newAttempt, CancellationToken ct = default)
    {
        var gateway = gateways.Default;
        var decided = await uow.ExecuteInTransactionAsync(token => DecideAsync(orderId, newAttempt, gateway.Name, token), ct);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        var d = decided.Value;
        if (d.Existing is { } ready)
        {
            return ready;
        }

        if (d.CancelInvoice is { } old)
        {
            await gateways.Find(d.CancelProvider!)!.CancelHostedPaymentAsync(old, ct);   // best effort
        }

        var token = statusTokens.Create(d.Reference).Token;
        var created = await gateway.CreateHostedPaymentAsync(new HostedPaymentRequest(d.PaymentId, d.Reference, d.AmountHalalas,
            d.Reference, links.OrderStatus(d.Locale, d.Reference, token), d.DueAt), ct);
        if (!created.Ok)
        {
            return PaymentErrors.GatewayUnavailable;
        }

        var attached = await uow.ExecuteInTransactionAsync(async t =>
        {
            var payment = await payments.GetByIdAsync(d.PaymentId, t);
            if (payment is not { IsOpen: true })
            {
                return Result.Failure<CheckoutPayment>(PaymentErrors.NotPayable(OrderStatus.PendingPayment));
            }

            payment.AttachHostedPage(created.ProviderInvoiceId!, created.RedirectUrl!, created.ExpiresAt, clock.UtcNow);
            return Result.Success(new CheckoutPayment(created.RedirectUrl!, created.ExpiresAt));
        }, ct);
        return attached;
    }

    private async Task<Result<Decision>> DecideAsync(Guid orderId, bool newAttempt, string provider, CancellationToken ct)
    {
        var order = await orders.GetForUpdateAsync(orderId, ct);
        var now = clock.UtcNow;
        if (order is null)
        {
            return OrderErrors.NotFound;
        }

        if (!order.IsPayable(now))
        {
            return PaymentErrors.NotPayable(order.Status);
        }

        var attempts = await payments.ForOrderAsync(order.Id, ct);
        var open = attempts.LastOrDefault(p => p.IsOpen);
        if (open is not null && open.Provider == provider && (open.RedirectUrl is null || !newAttempt))
        {
            return open.RedirectUrl is { } url
                ? new Decision(open.Id, order.Reference, order.Locale, Halalas(order), order.PaymentDueAt, new CheckoutPayment(url, open.RedirectExpiresAt), null, null)
                : new Decision(open.Id, order.Reference, order.Locale, Halalas(order), order.PaymentDueAt, null, null, null);
        }

        open?.Cancel(now);
        var attempt = Payment.Start(order.Id, attempts.Count == 0 ? 1 : attempts.Max(p => p.AttemptNo) + 1, provider, order.TotalAmount, now);
        payments.Add(attempt);
        return new Decision(attempt.Id, order.Reference, order.Locale, Halalas(order), order.PaymentDueAt, null, open?.ProviderInvoiceId, open?.Provider);
    }

    private static long Halalas(Order order) => order.TotalAmount.ToHalalas();

    private sealed record Decision(Guid PaymentId, string Reference, string Locale, long AmountHalalas, DateTimeOffset DueAt,
        CheckoutPayment? Existing, string? CancelInvoice, string? CancelProvider);
}
