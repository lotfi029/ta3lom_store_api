using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Domain.Entities.Donations;
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
}

internal sealed class OrderRepository(StoreDbContext db) : GenericRepository<Order>(db), IOrderRepository
{
    public async Task<long> NextReferenceSequenceAsync(CancellationToken ct = default) =>
        (await Db.Database.SqlQuery<long>($"""SELECT nextval('store.order_ref_seq') AS "Value" """).ToListAsync(ct))[0];

    public async Task<IReadOnlyList<Order>> ForCustomerAsync(Guid customerId, CancellationToken ct = default) =>
        await Set.Where(o => o.CustomerId == customerId).ToListAsync(ct);
}
