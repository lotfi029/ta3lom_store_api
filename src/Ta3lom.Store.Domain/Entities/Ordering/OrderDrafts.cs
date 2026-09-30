namespace Ta3lom.Store.Domain.Entities.Ordering;

/// A repriced line ready to become an `order_line` (RUL-15, RUL-24, RUL-27).
public sealed record OrderLineDraft(Guid ProductId, ProductType ProductType, Guid? BookFormatId, Guid? SponsorshipOfferId, LineKind Kind,
    LineMode Mode, int Quantity, Money UnitPrice, Money Amount, decimal VatRate, Money VatAmount, GiftIntent? Intent, string TitleAr, string? TitleEn,
    CartDedication? Dedication);

/// Contact snapshot (RUL-25): the verified mobile plus the name and email typed at checkout.
public sealed record OrderContact(string Name, string MobileE164, string Email);

/// Order-level shipping (RUL-26): the flat fee, its included VAT and the address snapshot.
public sealed record OrderShipping(Money Fee, Money Vat, ShippingAddress? Address);

public enum PaidOutcome
{
    Paid,
    AlreadyPaid,
    Review,
}
