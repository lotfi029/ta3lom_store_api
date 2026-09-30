namespace Ta3lom.Store.Domain.Entities.Carts;

/// What the visitor asked to add (11 §3.4 `AddCartLineRequest`, already parsed).
public sealed record CartLineSpec(Guid ProductId, Guid? BookFormatId, LineKind Kind, LineMode Mode, int Quantity, Money? Amount,
    GiftIntent? Intent, CartDedication? Dedication);

/// The catalogue facts a cart or checkout line is checked against (RUL-02, RUL-05, RUL-09/10, RUL-12, RUL-19, RUL-30).
public sealed record CartProduct(Guid ProductId, ProductType Type, string Slug, string TitleAr, string? TitleEn, bool IsPublished,
    Money? CoursePrice, CartFormat? Format, CartOffer? Offer, CartCause? Cause);

/// The requested book format (null when none was asked for or it belongs to another product).
public sealed record CartFormat(Guid Id, BookFormatKind Kind, Money Price, bool IsActive, int Available, bool HasFile);

/// The product's active (or, if none is active, latest) sponsorship offer.
public sealed record CartOffer(Guid Id, Money SeatPrice, bool AllowPartial, bool ZakatEligible, bool IsOpen, string UnitLabelAr, string UnitLabelEn);

public sealed record CartCause(Money? UnitAmount, string? UnitLabelAr, string? UnitLabelEn, bool AllowCustomAmount, Money MinAmount,
    bool ZakatEligible, bool IsOpen);

/// Settings that bound a cart (RUL-18, RUL-12 `min_gift_amount`, RUL-21).
public sealed record CartLimits(Money CartMaxTotal, Money MinGiftAmount, bool FreeTextEnabled, IReadOnlySet<string> TemplateKeys)
{
    public const int MaxLines = 50;
}

/// What the signed-in customer already has (RUL-13). Guests own nothing.
public sealed record CustomerOwnership(IReadOnlySet<Guid> OwnedCourses, IReadOnlyDictionary<Guid, string> UnpaidCourseOrders)
{
    public static CustomerOwnership None { get; } = new(new HashSet<Guid>(), new Dictionary<Guid, string>());

    public bool Owns(Guid productId) => OwnedCourses.Contains(productId);

    public string? UnpaidOrderFor(Guid productId) => UnpaidCourseOrders.GetValueOrDefault(productId);
}

/// A notice returned by the sign-in merge (11 §4.2 `cartNotices`).
public sealed record CartMergeResult(string Code, Guid ProductId);

/// Notice codes of the sign-in merge (11 §4.2 `cartNotices`); not errors. RUL-18 overflow re-uses the error codes.
public static class CartNotices
{
    public const string Combined = "cart.merge_combined";
    public const string DroppedOwnedCourse = "cart.merge_dropped_owned_course";
}
