using Ta3lom.Store.Domain.Entities.Payments;

namespace Ta3lom.Store.Domain.IRepositories;

public interface IPaymentRepository : IGenericRepository<Payment>
{
    /// Every attempt of the order, oldest first (tracked).
    Task<IReadOnlyList<Payment>> ForOrderAsync(Guid orderId, CancellationToken ct = default);

    /// Re-reads a tracked attempt (after the order row lock that serialises every payment change).
    Task ReloadAsync(Payment payment, CancellationToken ct = default);

    Task<Payment?> FindByProviderPaymentIdAsync(string provider, string providerPaymentId, CancellationToken ct = default);

    Task<Payment?> FindByProviderInvoiceIdAsync(string provider, string providerInvoiceId, CancellationToken ct = default);

    /// Reconciliation (review 04 P-6/A-5): attempts with a hosted page, started before `startedBefore`, not captured
    /// (initiated, cancelled or failed — a replaced or failed page can still be paid), of an order that is not settled
    /// (`pending_payment`, `payment_review`), or that expired/was cancelled less than 24 h after the page's own expiry
    /// (`lateAfter` = now − 24 h). Newest first.
    Task<IReadOnlyList<Payment>> ForReconciliationAsync(DateTimeOffset startedBefore, DateTimeOffset lateAfter, int max, CancellationToken ct = default);
}

public interface IPaymentEventRepository
{
    /// Inserts the event unless (provider, provider_event_id) exists (`ON CONFLICT DO NOTHING`); returns the stored row —
    /// the new one, or the earlier one for a replay.
    Task<(PaymentEvent Event, bool Inserted)> InsertOrGetAsync(PaymentEvent paymentEvent, CancellationToken ct = default);

    /// Inbox claim (review 04 S-5): true for the one delivery that may process the event — not processed, and not claimed
    /// since `staleBefore` (a claim left by a crash is taken over after <see cref="PaymentEvent.ClaimTimeout"/>).
    Task<bool> TryClaimAsync(Guid eventId, DateTimeOffset now, DateTimeOffset staleBefore, CancellationToken ct = default);

    /// Gives the claim back (the gateway could not be reached: the provider's retry may process it at once).
    Task ReleaseClaimAsync(Guid eventId, CancellationToken ct = default);

    /// A stored event, tracked (orphan resolution).
    Task<PaymentEvent?> GetAsync(Guid eventId, CancellationToken ct = default);
}

public interface IReceiptRepository : IGenericRepository<DonationReceipt>
{
    /// The order's donation receipt (DR), tracked.
    Task<DonationReceipt?> DonationReceiptForOrderAsync(Guid orderId, CancellationToken ct = default);

    /// Every receipt (DR and DRC) of these orders, tracked (D-14 anonymisation).
    Task<IReadOnlyList<DonationReceipt>> ForOrdersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken ct = default);
}

public interface IRefundRepository : IGenericRepository<Refund>
{
    /// The refund rows of one request (S-13: the key is the staff member's own): `key`, or `key:{attempt}` for a review
    /// refund that covered several payments. Tracked.
    Task<IReadOnlyList<Refund>> ForIdempotencyKeyAsync(Guid orderId, string requestedBy, string idempotencyKey, CancellationToken ct = default);

    /// Every refund of the order, tracked.
    Task<IReadOnlyList<Refund>> ForOrderAsync(Guid orderId, CancellationToken ct = default);

    /// Payments with a refund still `requested` since before `requestedBefore` (refund reconciliation, P-3).
    Task<IReadOnlyList<Guid>> PaymentsWithRequestedRefundsAsync(DateTimeOffset requestedBefore, int max, CancellationToken ct = default);
}
