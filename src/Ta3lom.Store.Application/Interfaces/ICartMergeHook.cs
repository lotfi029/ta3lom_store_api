namespace Ta3lom.Store.Application.Interfaces;

/// Called by VerifyOtp inside its transaction to merge the guest cart into the donor's cart (BR-A07).
public interface ICartMergeHook
{
    Task<IReadOnlyList<CartMergeNotice>> MergeGuestCartAsync(Guid? guestCartId, Guid customerId, CancellationToken ct = default);
}

/// What the merge changed, e.g. `cart.merge_combined` or `cart.merge_dropped` (a line over a limit).
public sealed record CartMergeNotice(string Code, Guid? LineId);
