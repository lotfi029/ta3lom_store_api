using Ta3lom.Store.Domain.Entities.Donations;

namespace Ta3lom.Store.Domain.IRepositories;

public interface ICartRepository : IGenericRepository<Cart>
{
    /// The cart with its lines, its row locked `FOR UPDATE` (every cart write and the checkout serialise on it).
    Task<Cart?> GetForUpdateAsync(Guid cartId, CancellationToken ct = default);

    /// The donor's cart (at most one, `ux_cart_customer`), locked like <see cref="GetForUpdateAsync"/>.
    Task<Cart?> GetForCustomerForUpdateAsync(Guid customerId, CancellationToken ct = default);

    /// The donor's cart, created first when there is none (L-4: `INSERT … ON CONFLICT DO NOTHING` on `ux_cart_customer`, so
    /// two parallel first writes end up on one cart), then locked and loaded.
    Task<Cart> GetOrCreateForCustomerForUpdateAsync(Guid customerId, DateTimeOffset now, CancellationToken ct = default);
}

public interface IOrderRepository : IGenericRepository<Order>
{
    /// `nextval('store.order_ref_seq')` (D-09).
    Task<long> NextReferenceSequenceAsync(CancellationToken ct = default);

    /// Every order of the donor (tracked), for anonymisation (D-14).
    Task<IReadOnlyList<Order>> ForCustomerAsync(Guid customerId, CancellationToken ct = default);

    /// The order a checkout of this donor with this `Idempotency-Key` placed, if any (A-5).
    Task<Order?> FindByIdempotencyKeyAsync(Guid customerId, string idempotencyKey, CancellationToken ct = default);
}
