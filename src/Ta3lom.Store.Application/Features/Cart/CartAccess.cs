using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using CartEntity = Ta3lom.Store.Domain.Entities.Carts.Cart;

namespace Ta3lom.Store.Application.Features.Cart;

/// Which cart a request works on (09 §5.9 `CurrentCartResolver`): a signed-in customer's active cart; otherwise the
/// guest cart named by the `t3s_cart` cookie while it is active, unexpired and not owned by a customer.
internal sealed class CartAccess(ICartRepository carts, IApplicationDbContext db, ICurrentActor actor, IClock clock)
{
    public Guid? CustomerId => actor.Kind == ActorKind.Customer ? actor.Id : null;

    /// Read-only view (no lock); null when the visitor has no usable cart.
    public async Task<CartEntity?> ForReadAsync(Guid? guestCartId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (CustomerId is { } customerId)
        {
            return await db.Carts.AsNoTracking().Include(c => c.Lines)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId && c.Status == CartStatus.Active, ct);
        }

        if (guestCartId is not { } id)
        {
            return null;
        }

        var cart = await db.Carts.AsNoTracking().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == id && c.CustomerId == null, ct);
        return cart is not null && cart.IsUsable(now) ? cart : null;
    }

    /// The cart to change, row-locked; null when the visitor has none. Call inside a unit of work.
    public async Task<CartEntity?> ExistingForWriteAsync(Guid? guestCartId, CancellationToken ct)
    {
        if (CustomerId is { } customerId)
        {
            return await carts.GetActiveForCustomerForUpdateAsync(customerId, ct);
        }

        if (guestCartId is not { } id)
        {
            return null;
        }

        var cart = await carts.GetForUpdateAsync(id, ct);
        return cart is { IsGuest: true } && cart.IsUsable(clock.UtcNow) ? cart : null;
    }

    /// The cart to add to: created on first use (guest row or the customer's active cart).
    public async Task<CartEntity> ForWriteAsync(Guid? guestCartId, CancellationToken ct)
    {
        if (CustomerId is { } customerId)
        {
            return await carts.GetOrCreateForCustomerForUpdateAsync(customerId, clock.UtcNow, ct);
        }

        var existing = await ExistingForWriteAsync(guestCartId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var cart = new CartEntity(clock.UtcNow);
        carts.Add(cart);
        return cart;
    }
}
