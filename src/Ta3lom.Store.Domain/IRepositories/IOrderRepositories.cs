using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Domain.IRepositories;

public interface IOrderRepository : IGenericRepository<Order>
{
    /// RUL-28: next value of the gap-tolerant `order_ref_seq`.
    Task<long> NextReferenceNumberAsync(CancellationToken ct = default);

    /// The order with its lines (tracked, not locked).
    Task<Order?> FindByReferenceAsync(string reference, CancellationToken ct = default);

    /// database/01 §4.4: `SELECT … FOR UPDATE` on the order row, then the order with its lines. Serialises settlement,
    /// cancel and expiry per order. Call inside a unit of work.
    Task<Order?> LoadForUpdateAsync(Guid orderId, CancellationToken ct = default);

    /// Unpaid orders past `payment_due_at` (RUL-29), oldest first.
    Task<IReadOnlyList<Guid>> DueForExpiryAsync(DateTimeOffset now, int batchSize, CancellationToken ct = default);
}

public interface IStockRepository
{
    /// RUL-30, database/01 §4.3: locks the print formats (in id order) and reserves each quantity with a conditional
    /// `UPDATE … WHERE stock_on_hand − stock_reserved ≥ q`. Returns the first format that could not be reserved, or null.
    /// Run inside the checkout unit of work: a failure must roll the whole transaction back.
    Task<Guid?> TryReserveAsync(IReadOnlyList<(Guid BookFormatId, int Quantity)> reservations, CancellationToken ct = default);

    /// RUL-30: gives reserved quantities back (expired / cancelled orders); never below zero.
    Task ReleaseAsync(IReadOnlyList<(Guid BookFormatId, int Quantity)> reservations, CancellationToken ct = default);
}

/// Goal counters, incremented atomically in the settlement transaction (09 §5.3), never read-modify-write.
public interface IGoalProgressRepository
{
    Task AddGiftAsync(Guid productId, Money raised, int units, int donors, Money zakat, DateTimeOffset now, CancellationToken ct = default);
}

/// RUL-59, 09 §5.10: gapless `SERIES-YYYY-NNNNNN` per series and Riyadh year; the counter row stays locked until the
/// transaction commits, and a rollback returns the number.
public interface IDocumentNumberAllocator
{
    Task<string> NextAsync(DocumentSeries series, int riyadhYear, CancellationToken ct = default);
}

public interface IEnrolmentRepository : IGenericRepository<Entities.Fulfilment.Enrolment>
{
    /// RUL-13/47c: inserts unless the customer already has a live enrolment for the course (`ux_enrolment_live`,
    /// `ON CONFLICT DO NOTHING`, so two settlements racing for the same course never fail). False = duplicate.
    Task<bool> TryAddLiveAsync(Entities.Fulfilment.Enrolment enrolment, CancellationToken ct = default);
}

public interface IDownloadGrantRepository : IGenericRepository<Entities.Fulfilment.DownloadGrant>
{
    /// RUL-41: `UPDATE … SET download_count = download_count + 1 WHERE not revoked AND download_count &lt; max_downloads`;
    /// false when the limit is reached or the grant is revoked (two parallel downloads can never pass the limit).
    Task<bool> TryConsumeAsync(Guid grantId, CancellationToken ct = default);
}
