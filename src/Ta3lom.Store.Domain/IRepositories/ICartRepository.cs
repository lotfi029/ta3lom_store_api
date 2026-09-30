using Ta3lom.Store.Domain.Entities.Carts;

namespace Ta3lom.Store.Domain.IRepositories;

/// Carts with their lines. The `…ForUpdate` reads lock the cart row (run them inside `ExecuteInTransactionAsync`).
public interface ICartRepository : IGenericRepository<Cart>
{
    /// The cart and its lines, row-locked; null when unknown.
    Task<Cart?> GetForUpdateAsync(Guid cartId, CancellationToken ct = default);

    /// The customer's active cart and its lines, row-locked; null when they have none.
    Task<Cart?> GetActiveForCustomerForUpdateAsync(Guid customerId, CancellationToken ct = default);

    /// The customer's active cart, created if missing (race-safe on `ux_cart_customer_active`), row-locked.
    Task<Cart> GetOrCreateForCustomerForUpdateAsync(Guid customerId, DateTimeOffset now, CancellationToken ct = default);

    /// RUL-16 + database/03 §5: guest carts past `expires_at` are deleted (lines cascade). Returns the number of carts.
    Task<int> DeleteExpiredGuestCartsAsync(DateTimeOffset now, CancellationToken ct = default);
}
