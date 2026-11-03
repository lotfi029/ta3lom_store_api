using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Domain.Entities.Donations;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.IRepositories;

namespace Ta3lom.Store.Infrastructure.Persistence.Repositories;

internal sealed class CartRepository(StoreDbContext db) : GenericRepository<Cart>(db), ICartRepository
{
    public async Task<Cart?> GetForUpdateAsync(Guid cartId, CancellationToken ct = default)
    {
        var locked = await Db.Database.SqlQuery<Guid>($"""SELECT id AS "Value" FROM store.cart WHERE id = {cartId} FOR UPDATE""").ToListAsync(ct);
        return locked.Count == 0 ? null : await Set.Include(c => c.Lines).FirstAsync(c => c.Id == cartId, ct);
    }

    public async Task<Cart?> GetForCustomerForUpdateAsync(Guid customerId, CancellationToken ct = default)
    {
        var locked = await Db.Database.SqlQuery<Guid>($"""SELECT id AS "Value" FROM store.cart WHERE customer_id = {customerId} FOR UPDATE""").ToListAsync(ct);
        return locked.Count == 0 ? null : await Set.Include(c => c.Lines).FirstAsync(c => c.Id == locked[0], ct);
    }

    public async Task<Cart> GetOrCreateForCustomerForUpdateAsync(Guid customerId, DateTimeOffset now, CancellationToken ct = default)
    {
        var fresh = Cart.CreateFor(customerId, now);
        // The conflict target and predicate match the partial unique index ux_cart_customer.
        await Db.Database.ExecuteSqlAsync($"""
            INSERT INTO store.cart (id, customer_id, created_at, updated_at, expires_at)
            VALUES ({fresh.Id}, {customerId}, {fresh.CreatedAt}, {fresh.UpdatedAt}, NULL)
            ON CONFLICT (customer_id) WHERE customer_id IS NOT NULL DO NOTHING
            """, ct);
        return await GetForCustomerForUpdateAsync(customerId, ct)
               ?? throw new InvalidOperationException($"The cart of customer {customerId} vanished after its insert.");
    }
}

internal sealed class OrderRepository(StoreDbContext db) : GenericRepository<Order>(db), IOrderRepository
{
    public async Task<long> NextReferenceSequenceAsync(CancellationToken ct = default) =>
        (await Db.Database.SqlQuery<long>($"""SELECT nextval('store.order_ref_seq') AS "Value" """).ToListAsync(ct))[0];

    public async Task<IReadOnlyList<Order>> ForCustomerAsync(Guid customerId, CancellationToken ct = default) =>
        await Set.Where(o => o.CustomerId == customerId).ToListAsync(ct);

    public Task<Order?> FindByIdempotencyKeyAsync(Guid customerId, string idempotencyKey, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(o => o.CustomerId == customerId && o.IdempotencyKey == idempotencyKey, ct);

    public async Task<Order?> GetForUpdateAsync(Guid orderId, CancellationToken ct = default)
    {
        var locked = await Db.Database.SqlQuery<Guid>($"""SELECT id AS "Value" FROM store.orders WHERE id = {orderId} FOR UPDATE""").ToListAsync(ct);
        return locked.Count == 0 ? null : await LoadLockedAsync(orderId, ct);
    }

    public async Task<Order?> GetByReferenceForUpdateAsync(string reference, CancellationToken ct = default)
    {
        var locked = await Db.Database.SqlQuery<Guid>($"""SELECT id AS "Value" FROM store.orders WHERE reference = {reference} FOR UPDATE""").ToListAsync(ct);
        return locked.Count == 0 ? null : await LoadLockedAsync(locked[0], ct);
    }

    public async Task<IReadOnlyList<Guid>> OverdueIdsAsync(DateTimeOffset now, int max, CancellationToken ct = default) =>
        await Set.AsNoTracking().Where(o => o.Status == OrderStatus.PendingPayment && o.PaymentDueAt <= now)
            .OrderBy(o => o.PaymentDueAt).Select(o => o.Id).Take(max).ToListAsync(ct);

    /// After the row lock: a tracked copy is reloaded so no value read before the lock survives.
    private async Task<Order> LoadLockedAsync(Guid orderId, CancellationToken ct)
    {
        if (Db.ChangeTracker.Entries<Order>().FirstOrDefault(e => e.Entity.Id == orderId) is { } tracked)
        {
            await tracked.ReloadAsync(ct);
        }

        return await Set.Include(o => o.Lines).FirstAsync(o => o.Id == orderId, ct);
    }
}
