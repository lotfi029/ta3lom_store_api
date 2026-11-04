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
    IDocumentNumbers numbers, IPaymentGatewayRegistry gateways, PaymentSettlement settlement, IOutbox outbox, IAuditTrail audit,
    ICurrentActor actor, IUnitOfWork uow, IClock clock)
{
    /// `amount` null = everything refundable on the payments in review (`reviewOnly`).
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
            if (await refunds.FindByIdempotencyKeyAsync(order.Id, idempotencyKey, t) is { } earlier)
            {
                return earlier.Status == RefundStatus.Requested
                    ? Result.Failure<Prepared>(Error.Conflict(ErrorCodes.IdempotencyInProgress, "This refund is in progress."))
                    : Result.Success(new Prepared(order.Id, [], earlier.Status == RefundStatus.Succeeded));
            }

            if (order.Version != version)
            {
                return Result.Failure<Prepared>(GeneralErrors.ConcurrencyConflict);
            }

            List<(Payment Payment, Money Amount)> plan;
            if (reviewOnly)
            {
                var inReview = attempts.Where(p => p.Status == PaymentStatus.Review && p.Refundable.IsPositive).ToList();
                if (inReview.Count == 0)
                {
                    return Result.Failure<Prepared>(PaymentErrors.NotInReview(order.Status));
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

                var source = attempts.Where(p => p.Status is PaymentStatus.Captured && p.Refundable.IsPositive).OrderByDescending(p => p.Refundable.Amount).FirstOrDefault();
                var refundable = attempts.Where(p => p.Status == PaymentStatus.Captured).Select(p => p.Refundable).Sum();
                if (source is null || amount!.Value > source.Refundable)
                {
                    return Result.Failure<Prepared>(PaymentErrors.RefundExceeds(source?.Refundable ?? refundable));
                }

                plan = [(source, amount.Value)];
            }

            var now = clock.UtcNow;
            var rows = plan.Select(p => Refund.Request(order.Id, p.Payment.Id, p.Amount, reason, staff, plan.Count == 1 ? idempotencyKey : $"{idempotencyKey}:{p.Payment.AttemptNo}", now)).ToList();
            foreach (var row in rows)
            {
                refunds.Add(row);
            }

            return Result.Success(new Prepared(order.Id, [.. rows.Zip(plan, (r, p) => new Planned(r.Id, p.Payment.Provider, p.Payment.ProviderPaymentId!, p.Amount))], false));
        }, ct);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        if (prepared.Value.Planned.Count == 0)
        {
            return prepared.Value.AlreadySucceeded
                ? await SnapshotAsync(prepared.Value.OrderId, ct)
                : PaymentErrors.RefundFailed;
        }

        var outcomes = new List<(Planned Planned, GatewayRefundResult Result)>();
        foreach (var planned in prepared.Value.Planned)
        {
            var gateway = gateways.Find(planned.Provider);
            outcomes.Add((planned, gateway is null
                ? GatewayRefundResult.Failed("unknown provider")
                : await gateway.RefundAsync(planned.ProviderPaymentId, planned.Amount.ToHalalas(), idempotencyKey, ct)));
        }

        var applied = await uow.ExecuteInTransactionAsync(async t =>
        {
            var order = (await orders.GetForUpdateAsync(prepared.Value.OrderId, t))!;
            var now = clock.UtcNow;
            var attempts = await payments.ForOrderAsync(order.Id, t);
            var anyFailed = false;
            var refundedOnOrder = Money.Zero;
            Guid? lastRefund = null;
            foreach (var (planned, result) in outcomes)
            {
                var refund = (await refunds.GetByIdAsync(planned.RefundId, t))!;
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
                var doublePayment = payment.Status == PaymentStatus.Review && order.Status is OrderStatus.Paid or OrderStatus.Refunded;
                payment.ApplyRefund(planned.Amount, now);
                if (!doublePayment)
                {
                    var captured = attempts.Where(p => p.IsCaptured && (order.Status == OrderStatus.PaymentReview || p.Status != PaymentStatus.Review || p.Id == payment.Id))
                        .Select(p => p.CapturedAmount).Sum();
                    order.ApplyRefund(planned.Amount, captured, now).IsSuccess.ShouldHold();
                    refundedOnOrder += planned.Amount;
                }

                lastRefund = refund.Id;
                audit.Record(reviewOnlyAction(), "orders", order.Id.ToString(), staff, new { order.Reference, amount = planned.Amount.ToWire(), refund = refund.Id });
            }

            Guid? drcId = null;
            if (order.Status == OrderStatus.Refunded && await receipts.DonationReceiptForOrderAsync(order.Id, t) is { VoidedByReceiptId: null } dr)
            {
                var year = clock.ToRiyadh(now).Year;
                var drc = DonationReceipt.IssueCancellation(dr, year, await numbers.NextAsync(ReceiptSeries.Drc, year, t), dr.Amount, now);
                receipts.Add(drc);
                outbox.Enqueue(new ReceiptIssued(drc.Id, order.Id, drc.DisplayNumber));
                drcId = drc.Id;
            }

            if (lastRefund is { } refundId && (refundedOnOrder.IsPositive || drcId is not null))
            {
                outbox.Enqueue(new RefundProcessed(order.Id, order.Reference, refundId, refundedOnOrder.ToWire(), order.Status == OrderStatus.Refunded, drcId));
            }

            return anyFailed && lastRefund is null ? Result.Failure<bool>(PaymentErrors.RefundFailed) : Result.Success(true);
        }, commitOnFailure: true, ct);
        return applied.IsFailure ? applied.Error : await SnapshotAsync(prepared.Value.OrderId, ct);

        string reviewOnlyAction() => reviewOnly ? AuditActions.PaymentReviewRefunded : AuditActions.OrderRefunded;
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

            var now = clock.UtcNow;
            var result = order.AcceptReview(now);
            if (result.IsFailure)
            {
                return Result.Failure<Guid>(result.Error);
            }

            foreach (var payment in (await payments.ForOrderAsync(order.Id, t)).Where(p => p.Status == PaymentStatus.Review))
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

    private async Task<Result<AdminPaymentActionResponse>> SnapshotAsync(Guid orderId, CancellationToken ct)
    {
        var order = (await orders.GetByIdAsync(orderId, ct))!;
        var receipt = await receipts.DonationReceiptForOrderAsync(orderId, ct);
        return new AdminPaymentActionResponse(order.Reference, order.Status.ToWire(), order.TotalAmount.ToWire(), order.RefundedAmount.ToWire(),
            order.Version, receipt?.DisplayNumber);
    }

    private sealed record Planned(Guid RefundId, string Provider, string ProviderPaymentId, Money Amount);

    private sealed record Prepared(Guid OrderId, IReadOnlyList<Planned> Planned, bool AlreadySucceeded);
}
