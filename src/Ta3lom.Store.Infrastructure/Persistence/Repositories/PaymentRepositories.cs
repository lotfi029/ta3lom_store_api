using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Entities.Payments;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.IRepositories;

namespace Ta3lom.Store.Infrastructure.Persistence.Repositories;

internal sealed class PaymentRepository(StoreDbContext db) : GenericRepository<Payment>(db), IPaymentRepository
{
    public async Task<IReadOnlyList<Payment>> ForOrderAsync(Guid orderId, CancellationToken ct = default) =>
        await Set.Where(p => p.OrderId == orderId).OrderBy(p => p.AttemptNo).ToListAsync(ct);

    public Task ReloadAsync(Payment payment, CancellationToken ct = default) => Db.Entry(payment).ReloadAsync(ct);

    public Task<Payment?> FindByProviderPaymentIdAsync(string provider, string providerPaymentId, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(p => p.Provider == provider && p.ProviderPaymentId == providerPaymentId, ct);

    public Task<Payment?> FindByProviderInvoiceIdAsync(string provider, string providerInvoiceId, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(p => p.Provider == provider && p.ProviderInvoiceId == providerInvoiceId, ct);

    public async Task<IReadOnlyList<Payment>> ForReconciliationAsync(DateTimeOffset startedBefore, DateTimeOffset lateAfter, int max, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(p => p.ProviderInvoiceId != null && p.InitiatedAt <= startedBefore
                        && (p.Status == PaymentStatus.Initiated || p.Status == PaymentStatus.Cancelled || p.Status == PaymentStatus.Failed))
            .Join(Db.Set<Order>(), p => p.OrderId, o => o.Id, (p, o) => new { p, o.Status, o.PaymentDueAt })
            .Where(x => x.Status == OrderStatus.PendingPayment || x.Status == OrderStatus.PaymentReview
                        || ((x.Status == OrderStatus.Expired || x.Status == OrderStatus.Cancelled) && (x.p.RedirectExpiresAt ?? x.PaymentDueAt) > lateAfter))
            .OrderByDescending(x => x.p.InitiatedAt).Select(x => x.p).Take(max).ToListAsync(ct);
}

/// The webhook inbox: `INSERT … ON CONFLICT (provider, provider_event_id) DO NOTHING`, then the stored row.
internal sealed class PaymentEventRepository(StoreDbContext db) : IPaymentEventRepository
{
    public async Task<(PaymentEvent Event, bool Inserted)> InsertOrGetAsync(PaymentEvent paymentEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paymentEvent);
        var e = paymentEvent;
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO store.payment_event (id, provider, provider_event_id, type, payment_id, provider_payment_id, received_at,
                                             signature_valid, processed_at, outcome, payload_json)
            VALUES ({e.Id}, {e.Provider}, {e.ProviderEventId}, {e.Type}, {e.PaymentId}, {e.ProviderPaymentId}, {e.ReceivedAt},
                    {e.SignatureValid}, {e.ProcessedAt}, {e.Outcome}, CAST({e.PayloadJson} AS jsonb))
            ON CONFLICT (provider, provider_event_id) DO NOTHING
            """, ct) == 1;
        var stored = await db.PaymentEvents.FirstAsync(x => x.Provider == e.Provider && x.ProviderEventId == e.ProviderEventId, ct);
        return (stored, inserted);
    }

    public async Task<bool> TryClaimAsync(Guid eventId, DateTimeOffset now, DateTimeOffset staleBefore, CancellationToken ct = default) =>
        await db.Database.ExecuteSqlAsync($"""
            UPDATE store.payment_event SET claimed_at = {now}
            WHERE id = {eventId} AND processed_at IS NULL AND (claimed_at IS NULL OR claimed_at < {staleBefore})
            """, ct) == 1;

    public Task ReleaseClaimAsync(Guid eventId, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"UPDATE store.payment_event SET claimed_at = NULL WHERE id = {eventId} AND processed_at IS NULL", ct);

    public Task<PaymentEvent?> GetAsync(Guid eventId, CancellationToken ct = default) =>
        db.PaymentEvents.FirstOrDefaultAsync(x => x.Id == eventId, ct);
}

internal sealed class ReceiptRepository(StoreDbContext db) : GenericRepository<DonationReceipt>(db), IReceiptRepository
{
    public Task<DonationReceipt?> DonationReceiptForOrderAsync(Guid orderId, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(r => r.OrderId == orderId && r.Series == ReceiptSeries.Dr, ct);

    public async Task<IReadOnlyList<DonationReceipt>> ForOrdersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken ct = default) =>
        await Set.Where(r => orderIds.Contains(r.OrderId)).ToListAsync(ct);
}

internal sealed class RefundRepository(StoreDbContext db) : GenericRepository<Refund>(db), IRefundRepository
{
    public async Task<IReadOnlyList<Refund>> ForIdempotencyKeyAsync(Guid orderId, string requestedBy, string idempotencyKey, CancellationToken ct = default)
    {
        var prefix = idempotencyKey + ":";
        return await Set.Where(r => r.OrderId == orderId && r.RequestedBy == requestedBy
                                    && (r.IdempotencyKey == idempotencyKey || r.IdempotencyKey.StartsWith(prefix)))
            .OrderBy(r => r.RequestedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Refund>> ForOrderAsync(Guid orderId, CancellationToken ct = default) =>
        await Set.Where(r => r.OrderId == orderId).OrderBy(r => r.RequestedAt).ThenBy(r => r.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> PaymentsWithRequestedRefundsAsync(DateTimeOffset requestedBefore, int max, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(r => r.Status == RefundStatus.Requested && r.RequestedAt <= requestedBefore)
            .Select(r => r.PaymentId).Distinct().Take(max).ToListAsync(ct);
}
