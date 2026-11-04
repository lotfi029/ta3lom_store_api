namespace Ta3lom.Store.Application.Features.Payments;

/// `ReconcilePayments` job (every 5 minutes, RUL-38, review 04 P-6/A-5): every hosted page of an attempt older than 5
/// minutes that is not captured — open, replaced (cancelled) or failed, since a donor can still pay a page in an old tab —
/// of an order that is not settled, or that expired/was cancelled less than 24 hours after the page's own expiry (a late
/// capture → review), is re-read from its gateway and settled through the same `PaymentSettlement` path as the webhook
/// — a missed webhook still settles the order exactly once. Returns the number of payments whose state changed.
public sealed record ReconcilePaymentsCommand : ICommand<int>;

internal sealed class ReconcilePaymentsCommandHandler(IPaymentRepository payments, IPaymentGatewayRegistry gateways,
    PaymentSettlement settlement, IClock clock) : ICommandHandler<ReconcilePaymentsCommand, int>
{
    public const int Batch = 100;

    public async Task<Result<int>> HandleAsync(ReconcilePaymentsCommand command, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var pages = (await payments.ForReconciliationAsync(now.AddMinutes(-5), now.AddHours(-24), Batch, ct))
            .Select(p => (p.Provider, Invoice: p.ProviderInvoiceId!)).Distinct().ToList();
        var changed = 0;
        foreach (var (provider, invoice) in pages)
        {
            changed += await PaymentReconciler.ReconcileAsync(provider, invoice, gateways, settlement, ct) ?? 0;
        }

        return changed;
    }
}

/// `ExpireUnpaidOrders` job (every 5 minutes): `pending_payment` orders past `paymentDueAt`. EVERY hosted page of the order
/// (open, replaced or failed attempts alike, P-6) is first re-read from the gateway — a capture right before the due time
/// settles instead of expiring. When any re-read fails (gateway down), the order is skipped and stays pending until the
/// next run (P-8): expiring it then could cancel a paid order. Otherwise, under the order lock, a still-pending order
/// expires, its open attempts are cancelled (and their hosted pages, best effort). The cart was emptied at checkout:
/// nothing is restored. Returns the number of expired orders.
public sealed record ExpireUnpaidOrdersCommand : ICommand<int>;

internal sealed class ExpireUnpaidOrdersCommandHandler(IOrderRepository orders, IPaymentRepository payments, IPaymentGatewayRegistry gateways,
    PaymentSettlement settlement, IUnitOfWork uow, IClock clock) : ICommandHandler<ExpireUnpaidOrdersCommand, int>
{
    public const int Batch = 200;

    public async Task<Result<int>> HandleAsync(ExpireUnpaidOrdersCommand command, CancellationToken ct = default)
    {
        var expired = 0;
        foreach (var orderId in await orders.OverdueIdsAsync(clock.UtcNow, Batch, ct))
        {
            var pages = (await payments.ForOrderAsync(orderId, ct)).Where(p => p.ProviderInvoiceId is not null)
                .Select(p => (p.Provider, Invoice: p.ProviderInvoiceId!)).Distinct().ToList();
            var unreachable = false;
            foreach (var (provider, invoice) in pages)
            {
                unreachable |= await PaymentReconciler.ReconcileAsync(provider, invoice, gateways, settlement, ct) is null;
            }

            if (unreachable)
            {
                continue;
            }

            var cancelled = new List<(string Provider, string Invoice)>();
            var result = await uow.ExecuteInTransactionAsync(async t =>
            {
                cancelled.Clear();
                var order = await orders.GetForUpdateAsync(orderId, t);
                var now = clock.UtcNow;
                if (order is null || order.Expire(now).IsFailure)
                {
                    return Result.Success(false);
                }

                foreach (var attempt in await payments.ForOrderAsync(orderId, t))
                {
                    await payments.ReloadAsync(attempt, t);
                    if (attempt.IsOpen)
                    {
                        attempt.Cancel(now);
                        if (attempt.ProviderInvoiceId is { } invoice)
                        {
                            cancelled.Add((attempt.Provider, invoice));
                        }
                    }
                }

                return Result.Success(true);
            }, ct);
            if (result.Value)
            {
                expired++;
                foreach (var (provider, invoice) in cancelled)
                {
                    if (gateways.Find(provider) is { } gateway)
                    {
                        await gateway.CancelHostedPaymentAsync(invoice, ct);
                    }
                }
            }
        }

        return expired;
    }
}

/// `ReconcileRefunds` job (every 5 minutes, review 04 P-3/A-6): payments with a refund still `requested` for more than a
/// minute (the finance call itself has a 20 s budget) are re-read from the gateway: a refund the gateway shows succeeds,
/// one it still does not show after `refund_pending_timeout_minutes` fails, and a difference nobody asked for is imported
/// as a refund made outside the store. Returns the number of payments whose refunds changed.
public sealed record ReconcileRefundsCommand : ICommand<int>;

internal sealed class ReconcileRefundsCommandHandler(IRefundRepository refunds, RefundReconciliation reconciliation, IClock clock)
    : ICommandHandler<ReconcileRefundsCommand, int>
{
    public const int Batch = 100;

    public async Task<Result<int>> HandleAsync(ReconcileRefundsCommand command, CancellationToken ct = default)
    {
        var changed = 0;
        foreach (var paymentId in await refunds.PaymentsWithRequestedRefundsAsync(clock.UtcNow.AddMinutes(-1), Batch, ct))
        {
            if (await reconciliation.ReconcileAsync(paymentId, ct) == true)
            {
                changed++;
            }
        }

        return changed;
    }
}

/// Re-reads every payment of one hosted page and settles each (webhook-equivalent). Returns how many changed, or null when
/// the gateway could not be reached (try next time).
internal static class PaymentReconciler
{
    public static async Task<int?> ReconcileAsync(string provider, string invoiceId, IPaymentGatewayRegistry gateways,
        PaymentSettlement settlement, CancellationToken ct)
    {
        if (gateways.Find(provider) is not { } gateway)
        {
            return 0;
        }

        IReadOnlyList<GatewayPayment> found;
        try
        {
            found = await gateway.FindPaymentsForHostedPageAsync(invoiceId, ct);
        }
        catch (PaymentGatewayException)
        {
            return null;
        }

        var changed = 0;
        foreach (var payment in found)
        {
            var (outcome, _) = await settlement.ApplyAsync(gateway.Name, payment, ct);
            if (outcome is PaymentSettlement.Settled or PaymentSettlement.Review or PaymentSettlement.FailedOutcome or PaymentSettlement.RefundImported)
            {
                changed++;
            }
        }

        return changed;
    }
}
