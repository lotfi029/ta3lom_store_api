using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.IRepositories;
using Ta3lom.Store.Domain.ValueObjects;

namespace Ta3lom.Store.Infrastructure.Persistence.Repositories;

internal sealed class OrderRepository(StoreDbContext db) : GenericRepository<Order>(db), IOrderRepository
{
    public async Task<long> NextReferenceNumberAsync(CancellationToken ct = default) =>
        (await Db.Database.SqlQuery<long>($"SELECT nextval('store.order_ref_seq') AS \"Value\"").ToListAsync(ct)).Single();

    public async Task<Order?> LoadForUpdateAsync(Guid orderId, CancellationToken ct = default)
    {
        // Never compose LINQ over locking SQL (09 §5.3): lock first, then load the graph.
        await Db.Database.ExecuteSqlAsync($"SELECT 1 FROM store.orders WHERE id = {orderId} FOR UPDATE", ct);
        return await Set.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == orderId, ct);
    }

    public async Task<IReadOnlyList<Guid>> DueForExpiryAsync(DateTimeOffset now, int batchSize, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(o => (o.Status == OrderStatus.PendingPayment || o.Status == OrderStatus.PaymentFailed) && o.PaymentDueAt < now)
            .OrderBy(o => o.PaymentDueAt).Select(o => o.Id).Take(batchSize).ToListAsync(ct);
}

/// Stock row locks (database/01 §4.3); the only place stock is reserved.
internal sealed class StockRepository(StoreDbContext db) : IStockRepository
{
    public async Task<Guid?> TryReserveAsync(IReadOnlyList<(Guid BookFormatId, int Quantity)> reservations, CancellationToken ct = default)
    {
        var ordered = reservations.GroupBy(r => r.BookFormatId).Select(g => (Id: g.Key, Quantity: g.Sum(r => r.Quantity))).OrderBy(r => r.Id).ToList();
        if (ordered.Count == 0)
        {
            return null;
        }

        var ids = ordered.Select(r => r.Id).ToArray();
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM store.book_format WHERE id = ANY({ids}) ORDER BY id FOR UPDATE", ct);
        foreach (var (id, quantity) in ordered)
        {
            var updated = await db.Database.ExecuteSqlAsync($"""
                UPDATE store.book_format SET stock_reserved = stock_reserved + {quantity}
                 WHERE id = {id} AND kind = 'print' AND stock_on_hand - stock_reserved >= {quantity}
                """, ct);
            if (updated == 0)
            {
                return id;
            }
        }

        return null;
    }

    public Task CommitSaleAsync(Guid bookFormatId, int quantity, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"""
            UPDATE store.book_format
               SET stock_on_hand = stock_on_hand - {quantity}, stock_reserved = GREATEST(stock_reserved - {quantity}, 0)
             WHERE id = {bookFormatId}
            """, ct);

    public async Task<bool> TryAdjustAsync(Guid bookFormatId, int delta, CancellationToken ct = default) =>
        await db.Database.ExecuteSqlAsync($"""
            UPDATE store.book_format SET stock_on_hand = coalesce(stock_on_hand, 0) + {delta}
             WHERE id = {bookFormatId} AND kind = 'print' AND coalesce(stock_on_hand, 0) + {delta} >= stock_reserved
            """, ct) == 1;

    public async Task ReleaseAsync(IReadOnlyList<(Guid BookFormatId, int Quantity)> reservations, CancellationToken ct = default)
    {
        foreach (var (id, quantity) in reservations.GroupBy(r => r.BookFormatId).Select(g => (g.Key, g.Sum(r => r.Quantity))).OrderBy(r => r.Key))
        {
            await db.Database.ExecuteSqlAsync($"""
                UPDATE store.book_format SET stock_reserved = GREATEST(stock_reserved - {quantity}, 0) WHERE id = {id}
                """, ct);
        }
    }
}

internal sealed class GoalProgressRepository(StoreDbContext db) : IGoalProgressRepository
{
    public Task AddGiftAsync(Guid productId, Money raised, int units, int donors, Money zakat, DateTimeOffset now, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO store.goal_progress (product_id, raised_amount, funded_units, donor_count, zakat_amount, last_gift_at, updated_at)
            VALUES ({productId}, {raised.Amount}, {units}, {donors}, {zakat.Amount}, {now}, {now})
            ON CONFLICT (product_id) DO UPDATE SET
                raised_amount = goal_progress.raised_amount + EXCLUDED.raised_amount,
                funded_units  = goal_progress.funded_units + EXCLUDED.funded_units,
                donor_count   = goal_progress.donor_count + EXCLUDED.donor_count,
                zakat_amount  = goal_progress.zakat_amount + EXCLUDED.zakat_amount,
                last_gift_at  = EXCLUDED.last_gift_at,
                updated_at    = EXCLUDED.updated_at
            """, ct);
}

internal sealed class DocumentNumberAllocator(StoreDbContext db) : IDocumentNumberAllocator
{
    public async Task<string> NextAsync(DocumentSeries series, int riyadhYear, CancellationToken ct = default)
    {
        var code = series.ToWire();                                                   // INV | CN | DR | DRC
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO store.document_counter (series, year) VALUES ({code}, {riyadhYear}) ON CONFLICT (series, year) DO NOTHING
            """, ct);
        var next = (await db.Database.SqlQuery<int>($"""
            UPDATE store.document_counter SET last_value = last_value + 1
             WHERE series = {code} AND year = {riyadhYear}
            RETURNING last_value AS "Value"
            """).ToListAsync(ct)).Single();                                           // the UPDATE holds the row lock until commit
        return FormattableString.Invariant($"{code}-{riyadhYear}-{next:D6}");
    }
}
