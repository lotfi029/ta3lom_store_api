using Ta3lom.Store.Application.DTOs.Donations;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Payments;

namespace Ta3lom.Store.Application.Features.Payments;

/// Opens a payment attempt on the gateway's hosted page (ADR-S05, plan §3.3, review 04 A-4). Three steps, the gateway call
/// OUTSIDE any transaction: (1) under the order lock, decide the attempt (reuse the open one, or create attempt n+1 and
/// cancel the previous open one); (2) call the gateway — every page expires at the order's `paymentDueAt`; (3) under the
/// order lock again, store the page on the attempt ONLY when it has none: a concurrent request that attached first wins,
/// and this request's new page is cancelled and the stored one returned — one live page per attempt. Pages of replaced
/// or failed attempts are cancelled at the gateway (best effort; they also expire at `paymentDueAt`). A gateway failure
/// leaves the order `pending_payment` with an open attempt without a page — the next call (checkout replay A-1, `/pay`)
/// tries again.
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

        foreach (var (provider, old) in d.Cancel)
        {
            if (gateways.Find(provider) is { } previous)
            {
                await previous.CancelHostedPaymentAsync(old, ct);                     // best effort
            }
        }

        var token = statusTokens.Create(d.Reference).Token;
        var created = await gateway.CreateHostedPaymentAsync(new HostedPaymentRequest(d.PaymentId, d.Reference, d.AmountHalalas,
            d.Reference, links.OrderStatus(d.Locale, d.Reference, token), d.DueAt), ct);
        if (!created.Ok)
        {
            return PaymentErrors.GatewayUnavailable;
        }

        var stale = false;
        var attached = await uow.ExecuteInTransactionAsync(async t =>
        {
            stale = false;
            await orders.GetForUpdateAsync(orderId, t);                              // serialised with every other /pay
            var payment = await payments.GetByIdAsync(d.PaymentId, t);
            if (payment is not null)
            {
                await payments.ReloadAsync(payment, t);
            }

            if (payment is not { IsOpen: true })
            {
                stale = true;
                return Result.Failure<CheckoutPayment>(PaymentErrors.NotPayable(OrderStatus.PendingPayment));
            }

            if (payment.ProviderInvoiceId is not null)
            {
                stale = true;                                                        // another request attached its page first
                return Result.Success(new CheckoutPayment(payment.RedirectUrl!, payment.RedirectExpiresAt));
            }

            payment.AttachHostedPage(created.ProviderInvoiceId!, created.RedirectUrl!, created.ExpiresAt, clock.UtcNow);
            return Result.Success(new CheckoutPayment(created.RedirectUrl!, created.ExpiresAt));
        }, ct);
        if (stale)
        {
            await gateway.CancelHostedPaymentAsync(created.ProviderInvoiceId!, ct);
        }

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
                ? new Decision(open.Id, order.Reference, order.Locale, Halalas(order), order.PaymentDueAt, new CheckoutPayment(url, open.RedirectExpiresAt), [])
                : new Decision(open.Id, order.Reference, order.Locale, Halalas(order), order.PaymentDueAt, null, []);
        }

        open?.Cancel(now);
        // P-6: the page of the replaced attempt, and of failed ones — a failed payment leaves its page payable.
        var cancel = attempts.Where(p => (p == open || p.Status == PaymentStatus.Failed) && p.ProviderInvoiceId is not null)
            .Select(p => (p.Provider, p.ProviderInvoiceId!)).Distinct().ToList();
        var attempt = Payment.Start(order.Id, attempts.Count == 0 ? 1 : attempts.Max(p => p.AttemptNo) + 1, provider, order.TotalAmount, now);
        payments.Add(attempt);
        return new Decision(attempt.Id, order.Reference, order.Locale, Halalas(order), order.PaymentDueAt, null, cancel);
    }

    private static long Halalas(Order order) => order.TotalAmount.ToHalalas();

    private sealed record Decision(Guid PaymentId, string Reference, string Locale, long AmountHalalas, DateTimeOffset DueAt,
        CheckoutPayment? Existing, IReadOnlyList<(string Provider, string Invoice)> Cancel);
}
