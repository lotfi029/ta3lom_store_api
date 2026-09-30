using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Domain.Entities.Carts;
using Ta3lom.Store.Domain.IRepositories;

namespace Ta3lom.Store.Infrastructure.Persistence.Repositories;

/// Row locks for cart changes (09 §5.9): two tabs adding at once cannot break the RUL-18 limits.
internal sealed class CartRepository(StoreDbContext db) : GenericRepository<Cart>(db), ICartRepository
{
    public async Task<Cart?> GetForUpdateAsync(Guid cartId, CancellationToken ct = default)
    {
        var carts = await Set.FromSql($"SELECT * FROM store.cart WHERE id = {cartId} FOR UPDATE").Include(c => c.Lines).ToListAsync(ct);
        return carts.SingleOrDefault();
    }

    public async Task<Cart?> GetActiveForCustomerForUpdateAsync(Guid customerId, CancellationToken ct = default)
    {
        var carts = await Set.FromSql($"SELECT * FROM store.cart WHERE customer_id = {customerId} AND status = 'active' FOR UPDATE")
            .Include(c => c.Lines).ToListAsync(ct);
        return carts.SingleOrDefault();
    }

    public async Task<Cart> GetOrCreateForCustomerForUpdateAsync(Guid customerId, DateTimeOffset now, CancellationToken ct = default)
    {
        var existing = await GetActiveForCustomerForUpdateAsync(customerId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var cart = Cart.ForCustomer(customerId, now);
        await Db.Database.ExecuteSqlAsync($"""
            INSERT INTO store.cart (id, customer_id, status, expires_at, created_at, updated_at)
            VALUES ({cart.Id}, {customerId}, 'active', NULL, {now}, {now})
            ON CONFLICT (customer_id) WHERE status = 'active' AND customer_id IS NOT NULL DO NOTHING
            """, ct);
        return await GetActiveForCustomerForUpdateAsync(customerId, ct)
               ?? throw new InvalidOperationException("The customer cart vanished after its insert.");
    }

    public Task<int> DeleteExpiredGuestCartsAsync(DateTimeOffset now, CancellationToken ct = default) =>
        Set.Where(c => c.CustomerId == null && c.ExpiresAt < now).ExecuteDeleteAsync(ct);
}
