namespace Ta3lom.Store.Application.Features.Payments;

/// `ReconcilePayments` job (every 5 minutes, RUL-38): attempts still `initiated` for more than 5 minutes whose order is
/// due less than 24 hours ago are re-read from their gateway (payments of the hosted page) and settled through the same
/// `PaymentSettlement` path as the webhook — a missed webhook still settles the order exactly once. Returns the number
/// of attempts whose state changed.
public sealed record ReconcilePaymentsCommand : ICommand<int>;

internal sealed class ReconcilePaymentsCommandHandler(IPaymentRepository payments, IPaymentGatewayRegistry gateways,
    PaymentSettlement settlement, IClock clock) : ICommandHandler<ReconcilePaymentsCommand, int>
{
    public const int Batch = 100;

    public async Task<Result<int>> HandleAsync(ReconcilePaymentsCommand command, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var open = await payments.OpenForReconciliationAsync(now.AddMinutes(-5), now.AddHours(-24), Batch, ct);
        var changed = 0;
        foreach (var attempt in open)
        {
            changed += await PaymentReconciler.ReconcileAsync(attempt.Provider, attempt.ProviderInvoiceId!, gateways, settlement, ct);
        }

        return changed;
    }
}

/// `ExpireUnpaidOrders` job (every 5 minutes): `pending_payment` orders past `paymentDueAt`. Each open attempt is first
/// re-read from the gateway (a capture right before the due time settles instead of expiring); then, under the order lock,
/// a still-pending order expires, its open attempts are cancelled (and their hosted pages, best effort). The cart was
/// emptied at checkout: nothing is restored. Returns the number of expired orders.
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
            foreach (var open in (await payments.ForOrderAsync(orderId, ct)).Where(p => p.IsOpen && p.ProviderInvoiceId is not null).ToList())
            {
                await PaymentReconciler.ReconcileAsync(open.Provider, open.ProviderInvoiceId!, gateways, settlement, ct);
            }

            var cancelled = new List<(string Provider, string Invoice)>();
            var result = await uow.ExecuteInTransactionAsync(async t =>
            {
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

/// Re-reads every payment of one hosted page and settles each (webhook-equivalent). Gateway down → 0, try next time.
internal static class PaymentReconciler
{
    public static async Task<int> ReconcileAsync(string provider, string invoiceId, IPaymentGatewayRegistry gateways,
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
            return 0;
        }

        var changed = 0;
        foreach (var payment in found)
        {
            var (outcome, _) = await settlement.ApplyAsync(gateway.Name, payment, ct);
            if (outcome is PaymentSettlement.Settled or PaymentSettlement.Review or PaymentSettlement.FailedOutcome)
            {
                changed++;
            }
        }

        return changed;
    }
}
