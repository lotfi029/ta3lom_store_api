using FluentValidation;
using Ta3lom.Store.Application.Abstractions.Wire;
using Ta3lom.Store.Application.DTOs.AdminOps;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Payments;
using Ta3lom.Store.Domain.Events;

namespace Ta3lom.Store.Application.Features.Payments;

/// `POST /v1/admin/orders/{reference}/refunds` (F-D5; `staff:finance`): refund an amount of the order's captured payment.
/// (1) under the order lock: version, status (`paid` only — a payment in review goes through the review actions), amount
/// ≤ refundable, a `refund` row `requested` (unique per order and Idempotency-Key); (2) the gateway call OUTSIDE the
/// transaction; (3) under the lock again: success → refund `succeeded`, payment and order amounts, full → `refunded` +
/// DRC voiding the DR, outbox `RefundProcessed`; failure → refund `failed`, 502 `payment.refund_failed`, nothing else
/// changes. Audited either way.
public sealed record RefundOrderCommand(string Reference, AdminRefundRequest Request, string IdempotencyKey) : ICommand<AdminPaymentActionResponse>;

internal sealed class RefundOrderCommandHandler(FinanceOperations finance) : ICommandHandler<RefundOrderCommand, AdminPaymentActionResponse>
{
    public async Task<Result<AdminPaymentActionResponse>> HandleAsync(RefundOrderCommand command, CancellationToken ct = default)
    {
        if (!MoneyInput.TryParse(command.Request.Amount, out var amount) || !amount.IsPositive)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["amount"] = ["A positive amount with two decimals."] });
        }

        return await finance.RefundAsync(command.Reference, command.Request.Version, amount, command.Request.Reason, command.IdempotencyKey,
            reviewOnly: false, ct);
    }
}

internal sealed class RefundOrderCommandValidator : AbstractValidator<RefundOrderCommand>
{
    public RefundOrderCommandValidator()
    {
        RuleFor(c => c.Request.Reason).NotEmpty().MaximumLength(500);
        RuleFor(c => c.Request.Amount).NotEmpty().MaximumLength(20);
        RuleFor(c => c.IdempotencyKey).NotEmpty().MaximumLength(64);
    }
}

/// `POST /v1/admin/orders/{reference}/review/accept` (RUL-29/35; `staff:finance`): finance accepts the payment in review —
/// the order is settled in full (`paid`), a DR is issued and the donor gets the paid messages.
public sealed record AcceptPaymentReviewCommand(string Reference, AdminReviewRequest Request) : ICommand<AdminPaymentActionResponse>;

internal sealed class AcceptPaymentReviewCommandHandler(FinanceOperations finance) : ICommandHandler<AcceptPaymentReviewCommand, AdminPaymentActionResponse>
{
    public Task<Result<AdminPaymentActionResponse>> HandleAsync(AcceptPaymentReviewCommand command, CancellationToken ct = default) =>
        finance.AcceptReviewAsync(command.Reference, command.Request.Version, ct);
}

/// `POST /v1/admin/orders/{reference}/review/refund` (`staff:finance`): refund everything captured by the payments in
/// review. An order in `payment_review` becomes `refunded`; a double payment on a `paid` order refunds only the extra
/// payment (the order stays `paid`). `Idempotency-Key` required.
public sealed record RefundPaymentReviewCommand(string Reference, AdminReviewRequest Request, string IdempotencyKey) : ICommand<AdminPaymentActionResponse>;

internal sealed class RefundPaymentReviewCommandHandler(FinanceOperations finance) : ICommandHandler<RefundPaymentReviewCommand, AdminPaymentActionResponse>
{
    public Task<Result<AdminPaymentActionResponse>> HandleAsync(RefundPaymentReviewCommand command, CancellationToken ct = default) =>
        finance.RefundAsync(command.Reference, command.Request.Version, null, command.Request.Reason ?? "payment review", command.IdempotencyKey,
            reviewOnly: true, ct);
}

/// The finance actions share the locking and the receipts.
public sealed class FinanceOperations(IOrderRepository orders, IPaymentRepository payments, IRefundRepository refunds, IReceiptRepository receipts,
    IPaymentGatewayRegistry gateways, PaymentSettlement settlement, RefundBook book, RefundReconciliation reconciliation, IOutbox outbox,
    IAuditTrail audit, ICurrentActor actor, IUnitOfWork uow, IClock clock)
{
    /// P-3: the refund call has its own budget, not the request's — a client that goes away must not leave the refund
    /// unknown halfway; past it the refund stays `requested` and the reconciliation settles it.
    public static readonly TimeSpan RefundCallTimeout = TimeSpan.FromSeconds(20);

    public const string RefundPendingState = "pending";
    public const string RefundSucceededState = "succeeded";

    /// `amount` null = everything refundable on the payments in review (`reviewOnly`).
    /// (1) under the order lock: the staff member's own key (S-13) — a refund still `requested` with that key is reconciled
    /// against the gateway instead of being sent again; status; amount ≤ captured − succeeded − requested (P-4); no other
    /// refund pending (A-6); version; `requested` rows. (2) The gateway call outside the transaction with its own timeout.
    /// (3) under the lock again, per row: succeeded → <see cref="RefundBook"/>; refused → `failed` (502 when nothing
    /// succeeded); no definite answer → stays `requested` and the answer is 202 `refund: "pending"` (P-3).
    public async Task<Result<AdminPaymentActionResponse>> RefundAsync(string reference, uint version, Money? amount, string reason,
        string idempotencyKey, bool reviewOnly, CancellationToken ct)
    {
        var staff = actor.Name ?? "staff";
        var prepared = await uow.ExecuteInTransactionAsync(async t =>
        {
            var order = await orders.GetByReferenceForUpdateAsync(Order.NormalizeReference(reference), t);
            if (order is null)
            {
                return Result.Failure<Prepared>(OrderErrors.NotFound);
            }

            var attempts = await payments.ForOrderAsync(order.Id, t);
            if (await refunds.ForIdempotencyKeyAsync(order.Id, staff, idempotencyKey, t) is { Count: > 0 } earlier)
            {
                return Result.Success(new Prepared(order.Id, [], earlier.Any(r => r.Status == RefundStatus.Succeeded),
                    [.. earlier.Where(r => r.Status == RefundStatus.Requested).Select(r => r.PaymentId).Distinct()]));
            }

            var requested = (await refunds.ForOrderAsync(order.Id, t)).Where(r => r.Status == RefundStatus.Requested).ToList();
            Money Available(Payment p) => p.Refundable - requested.Where(r => r.PaymentId == p.Id).Select(r => r.Amount).Sum();

            List<(Payment Payment, Money Amount)> plan;
            if (reviewOnly)
            {
                var inReview = attempts.Where(p => p.Status == PaymentStatus.Review && p.Refundable.IsPositive).ToList();
                if (inReview.Count == 0)
                {
                    return Result.Failure<Prepared>(PaymentErrors.NotInReview(order.Status));
                }

                if (requested.Count > 0)
                {
                    return Result.Failure<Prepared>(PaymentErrors.RefundPending);
                }

                plan = [.. inReview.Select(p => (p, p.Refundable))];
            }
            else
            {
                if (order.Status == OrderStatus.PaymentReview)
                {
                    return Result.Failure<Prepared>(PaymentErrors.ReviewRequired);
                }

                if (order.Status != OrderStatus.Paid)
                {
                    return Result.Failure<Prepared>(PaymentErrors.RefundNotAllowed(order.Status));
                }

                var captured = attempts.Where(p => p.Status == PaymentStatus.Captured).ToList();
                var source = captured.Where(p => Available(p).IsPositive).OrderByDescending(p => Available(p).Amount).FirstOrDefault();
                var refundable = captured.Select(Available).Where(m => m.IsPositive).Sum();
                if (source is null || amount!.Value > Available(source))
                {
                    return Result.Failure<Prepared>(PaymentErrors.RefundExceeds(source is null ? refundable : Available(source)));
                }

                if (requested.Count > 0)
                {
                    return Result.Failure<Prepared>(PaymentErrors.RefundPending);
                }

                plan = [(source, amount.Value)];
            }

            if (order.Version != version)
            {
                return Result.Failure<Prepared>(GeneralErrors.ConcurrencyConflict);
            }

            var now = clock.UtcNow;
            var rows = plan.Select(p => Refund.Request(order.Id, p.Payment.Id, p.Amount, reason, staff, plan.Count == 1 ? idempotencyKey : $"{idempotencyKey}:{p.Payment.AttemptNo}", now)).ToList();
            foreach (var row in rows)
            {
                refunds.Add(row);
            }

            return Result.Success(new Prepared(order.Id, [.. rows.Zip(plan, (r, p) => new Planned(r.Id, p.Payment.Provider, p.Payment.ProviderPaymentId!, p.Amount))], false, []));
        }, ct);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        var ready = prepared.Value;
        if (ready.Planned.Count == 0)
        {
            return await RetryAsync(ready, ct);
        }

        var outcomes = new List<(Planned Planned, GatewayRefundResult Result)>();
        foreach (var planned in ready.Planned)
        {
            outcomes.Add((planned, await CallGatewayAsync(planned, idempotencyKey)));
        }

        var applied = await uow.ExecuteInTransactionAsync(async t =>
        {
            var order = (await orders.GetForUpdateAsync(ready.OrderId, t))!;
            var now = clock.UtcNow;
            var attempts = await payments.ForOrderAsync(order.Id, t);
            var anyFailed = false;
            var anyPending = false;
            var refundedOnOrder = Money.Zero;
            Guid? lastRefund = null;
            foreach (var (planned, result) in outcomes)
            {
                var refund = (await refunds.GetByIdAsync(planned.RefundId, t))!;
                if (refund.Status != RefundStatus.Requested)
                {
                    lastRefund ??= refund.Status == RefundStatus.Succeeded ? refund.Id : null;   // a webhook confirmed it meanwhile
                    continue;
                }

                if (result.Pending)
                {
                    anyPending = true;
                    audit.Record(AuditActions.OrderRefundPending, "orders", order.Id.ToString(), staff, new { order.Reference, amount = planned.Amount.ToWire(), result.Error });
                    continue;
                }

                if (!result.Ok)
                {
                    refund.Fail(result.Error, now);
                    anyFailed = true;
                    audit.Record(AuditActions.OrderRefundFailed, "orders", order.Id.ToString(), staff, new { order.Reference, amount = planned.Amount.ToWire(), result.Error });
                    continue;
                }

                refund.Succeed(result.ProviderRefundId, now);
                var payment = attempts.First(p => p.Id == refund.PaymentId);
                await payments.ReloadAsync(payment, t);
                refundedOnOrder += RefundBook.Apply(order, attempts, payment, planned.Amount, now);
                lastRefund = refund.Id;
                audit.Record(reviewOnly ? AuditActions.PaymentReviewRefunded : AuditActions.OrderRefunded, "orders", order.Id.ToString(), staff,
                    new { order.Reference, amount = planned.Amount.ToWire(), refund = refund.Id });
            }

            await book.FinishAsync(order, lastRefund, refundedOnOrder, now, t);
            return anyFailed && lastRefund is null && !anyPending ? Result.Failure<bool>(PaymentErrors.RefundFailed) : Result.Success(anyPending);
        }, commitOnFailure: true, ct);
        return applied.IsFailure ? applied.Error : await SnapshotAsync(ready.OrderId, applied.Value ? RefundPendingState : RefundSucceededState, ct);
    }

    private async Task<GatewayRefundResult> CallGatewayAsync(Planned planned, string idempotencyKey)
    {
        if (gateways.Find(planned.Provider) is not { } gateway)
        {
            return GatewayRefundResult.Failed("unknown provider");
        }

        using var budget = new CancellationTokenSource(RefundCallTimeout);
        try
        {
            return await gateway.RefundAsync(planned.ProviderPaymentId, planned.Amount.ToHalalas(), idempotencyKey, budget.Token);
        }
        catch (OperationCanceledException)
        {
            return GatewayRefundResult.Unknown("timeout");
        }
    }

    /// The same key again: nothing is sent twice. Rows still `requested` are reconciled against the gateway first.
    private async Task<Result<AdminPaymentActionResponse>> RetryAsync(Prepared ready, CancellationToken ct)
    {
        foreach (var paymentId in ready.Pending)
        {
            await reconciliation.ReconcileAsync(paymentId, ct);
        }

        if (ready.Pending.Count > 0)
        {
            var rows = (await refunds.ForOrderAsync(ready.OrderId, ct)).Where(r => ready.Pending.Contains(r.PaymentId)).ToList();
            if (rows.Any(r => r.Status == RefundStatus.Requested))
            {
                return await SnapshotAsync(ready.OrderId, RefundPendingState, ct);
            }

            if (!rows.Any(r => r.Status == RefundStatus.Succeeded))
            {
                return PaymentErrors.RefundFailed;
            }
        }
        else if (!ready.AlreadySucceeded)
        {
            return PaymentErrors.RefundFailed;
        }

        return await SnapshotAsync(ready.OrderId, RefundSucceededState, ct);
    }

    public async Task<Result<AdminPaymentActionResponse>> AcceptReviewAsync(string reference, uint version, CancellationToken ct)
    {
        var staff = actor.Name ?? "staff";
        var accepted = await uow.ExecuteInTransactionAsync(async t =>
        {
            var order = await orders.GetByReferenceForUpdateAsync(Order.NormalizeReference(reference), t);
            if (order is null)
            {
                return Result.Failure<Guid>(OrderErrors.NotFound);
            }

            if (order.Version != version)
            {
                return Result.Failure<Guid>(GeneralErrors.ConcurrencyConflict);
            }

            var attempts = await payments.ForOrderAsync(order.Id, t);
            var kept = attempts.Where(p => p.IsCaptured).Select(p => p.CapturedAmount - p.RefundedAmount).Sum();
            if (order.Status == OrderStatus.PaymentReview && kept < order.TotalAmount)
            {
                // P-9 (REVIEW): a DR may not state more than the gateway kept; finance refunds the review instead.
                return Result.Failure<Guid>(PaymentErrors.ReviewPartial(kept, order.TotalAmount));
            }

            var now = clock.UtcNow;
            var result = order.AcceptReview(now);
            if (result.IsFailure)
            {
                return Result.Failure<Guid>(result.Error);
            }

            foreach (var payment in attempts.Where(p => p.Status == PaymentStatus.Review))
            {
                payment.AcceptReview(now);
            }

            var receipt = await receipts.DonationReceiptForOrderAsync(order.Id, t) ?? await settlement.IssueDonationReceiptAsync(order, now, t);
            outbox.Enqueue(new OrderPaid(order.Id, order.Reference, order.TotalAmount.ToWire(), receipt.Id));
            outbox.Enqueue(new ReceiptIssued(receipt.Id, order.Id, receipt.DisplayNumber));
            audit.Record(AuditActions.PaymentReviewAccepted, "orders", order.Id.ToString(), staff, new { order.Reference });
            return Result.Success(order.Id);
        }, ct);
        return accepted.IsFailure ? accepted.Error : await SnapshotAsync(accepted.Value, ct);
    }

    private async Task<Result<AdminPaymentActionResponse>> SnapshotAsync(Guid orderId, CancellationToken ct) => await SnapshotAsync(orderId, null, ct);

    private async Task<Result<AdminPaymentActionResponse>> SnapshotAsync(Guid orderId, string? refund, CancellationToken ct)
    {
        var order = (await orders.GetByIdAsync(orderId, ct))!;
        var receipt = await receipts.DonationReceiptForOrderAsync(orderId, ct);
        return new AdminPaymentActionResponse(order.Reference, order.Status.ToWire(), order.TotalAmount.ToWire(), order.RefundedAmount.ToWire(),
            order.Version, receipt?.DisplayNumber, refund);
    }

    private sealed record Planned(Guid RefundId, string Provider, string ProviderPaymentId, Money Amount);

    /// `Pending` = payments whose refund with this key is still `requested` (a retry reconciles them).
    private sealed record Prepared(Guid OrderId, IReadOnlyList<Planned> Planned, bool AlreadySucceeded, IReadOnlyList<Guid> Pending);
}
