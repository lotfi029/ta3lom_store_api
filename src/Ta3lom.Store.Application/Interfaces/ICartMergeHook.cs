namespace Ta3lom.Store.Application.Interfaces;

/// Hook called by VerifyOtp to merge the guest cart into the customer's cart (RUL-17).
/// S-04 registers a no-op; S-13 replaces it with the real merge.
public interface ICartMergeHook
{
    Task<IReadOnlyList<CartMergeNotice>> MergeGuestCartAsync(Guid? guestCartId, Guid customerId, CancellationToken ct = default);
}

/// `cart.merge_dropped_owned_course`, `cart.merge_combined` … (11 §4.2).
public sealed record CartMergeNotice(string Code, string Slug);
