using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Domain.IRepositories;

namespace Ta3lom.Store.Infrastructure.Persistence.Repositories;

internal sealed class OrderRepository(StoreDbContext db) : GenericRepository<Order>(db), IOrderRepository
{
    public async Task<long> NextReferenceNumberAsync(CancellationToken ct = default) =>
        (await Db.Database.SqlQuery<long>($"SELECT nextval('store.order_ref_seq') AS \"Value\"").ToListAsync(ct)).Single();

    public Task<Order?> FindByReferenceAsync(string reference, CancellationToken ct = default) =>
        Set.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Reference == reference, ct);
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
}
