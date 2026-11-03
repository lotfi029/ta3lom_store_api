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

    /// Attempts still `initiated`, started before `startedBefore`, whose order is not past `dueBefore` (reconciliation).
    Task<IReadOnlyList<Payment>> OpenForReconciliationAsync(DateTimeOffset startedBefore, DateTimeOffset dueAfter, int max, CancellationToken ct = default);
}

public interface IPaymentEventRepository
{
    /// Inserts the event unless (provider, provider_event_id) exists (`ON CONFLICT DO NOTHING`); returns the stored row —
    /// the new one, or the earlier one for a replay.
    Task<(PaymentEvent Event, bool Inserted)> InsertOrGetAsync(PaymentEvent paymentEvent, CancellationToken ct = default);
}

public interface IReceiptRepository : IGenericRepository<DonationReceipt>
{
    /// The order's donation receipt (DR), tracked.
    Task<DonationReceipt?> DonationReceiptForOrderAsync(Guid orderId, CancellationToken ct = default);
}

public interface IRefundRepository : IGenericRepository<Refund>
{
    Task<Refund?> FindByIdempotencyKeyAsync(Guid orderId, string idempotencyKey, CancellationToken ct = default);
}
