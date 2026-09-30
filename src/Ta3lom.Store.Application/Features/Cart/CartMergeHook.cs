using Ta3lom.Store.Domain.Entities.Carts;

namespace Ta3lom.Store.Application.Features.Cart;

/// RUL-17, called by VerifyOtp inside its unit of work: the guest cart is merged into the customer's active cart, or
/// becomes it when they have none. Owned courses are dropped with a notice (11 §4.2 `cartNotices`).
internal sealed class CartMergeHook(ICartRepository carts, CartCatalog catalog, IClock clock) : ICartMergeHook
{
    public async Task<IReadOnlyList<CartMergeNotice>> MergeGuestCartAsync(Guid? guestCartId, Guid customerId, CancellationToken ct = default)
    {
        if (guestCartId is not { } id)
        {
            return [];
        }

        var now = clock.UtcNow;
        var guest = await carts.GetForUpdateAsync(id, ct);
        if (guest is null || !guest.IsGuest || !guest.IsUsable(now))
        {
            return [];
        }

        var owned = await catalog.OwnershipAsync(customerId, ct);
        var target = await carts.GetActiveForCustomerForUpdateAsync(customerId, ct);
        var keys = guest.Lines.Concat(target?.Lines ?? []).Select(l => (l.ProductId, l.BookFormatId)).ToList();
        var snapshots = await catalog.SnapshotsAsync(keys, ct);

        IReadOnlyList<CartMergeResult> results;
        if (target is null)
        {
            results = guest.AssignTo(customerId, owned, now);
        }
        else
        {
            var limits = await catalog.LimitsAsync(ct);
            results = guest.MergeInto(target, owned, line => snapshots.TryGetValue((line.ProductId, line.BookFormatId), out var p)
                ? CartLineRules.MaxQuantity(line.Kind, p)
                : line.Quantity, limits, now);
        }

        var slugs = snapshots.Values.GroupBy(p => p.ProductId).ToDictionary(g => g.Key, g => g.First().Slug);
        return [.. results.Select(r => new CartMergeNotice(r.Code, slugs.GetValueOrDefault(r.ProductId, string.Empty)))];
    }
}
