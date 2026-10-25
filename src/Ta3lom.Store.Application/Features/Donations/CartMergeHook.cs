using Ta3lom.Store.Domain.Entities.Donations;

namespace Ta3lom.Store.Application.Features.Donations;

/// Sign-in merge (BR-A07), inside the OTP verify transaction: both carts are locked (guest first), the guest lines move
/// into the donor's cart (option lines combine, a line over a limit is dropped) and the guest cart is deleted. The notices
/// report what was combined or dropped.
internal sealed class CartMergeHook(ICartRepository carts, IStoreSettings settings, IUnitOfWork uow, IClock clock) : ICartMergeHook
{
    public const string Combined = "cart.merge_combined";
    public const string Dropped = "cart.merge_dropped";

    public async Task<IReadOnlyList<CartMergeNotice>> MergeGuestCartAsync(Guid? guestCartId, Guid customerId, CancellationToken ct = default)
    {
        if (guestCartId is not { } id || await carts.GetForUpdateAsync(id, ct) is not { IsGuest: true } guest)
        {
            return [];
        }

        var now = clock.UtcNow;
        if (guest.Lines.Count == 0 || guest.ExpiresAt <= now)
        {
            carts.Remove(guest);
            await uow.SaveChangesAsync(ct);
            return [];
        }

        var donor = await carts.GetOrCreateForCustomerForUpdateAsync(customerId, now, ct);

        var outcomes = donor.MergeFrom(guest, CartPricer.Limits(await settings.GetAsync(ct)), now);
        carts.Remove(guest);
        await uow.SaveChangesAsync(ct);
        return [.. outcomes.Where(o => o.Kind != CartMergeOutcome.Moved)
            .Select(o => new CartMergeNotice(o.Kind == CartMergeOutcome.Combined ? Combined : Dropped, o.LineId))];
    }
}
