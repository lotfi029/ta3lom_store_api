using Ta3lom.Store.Domain.Entities.Carts;
using Ta3lom.Store.Domain.Enums;
using Ta3lom.Store.Domain.ValueObjects;

namespace Ta3lom.Store.Domain.Tests.Carts;

/// Catalogue facts and line specs for the cart tests (RUL-12 matrix).
internal static class CartData
{
    public static readonly DateTimeOffset T0 = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    public static readonly Money Seat = new(350m);

    public static CartLimits Limits(decimal maxTotal = 100_000m, bool freeText = false) =>
        new(new Money(maxTotal), new Money(10m), freeText, new HashSet<string>(StringComparer.Ordinal) { "eid", "ramadan" });

    public static CartProduct Course(bool published = true, decimal? price = 350m, CartOffer? offer = null) =>
        new(Guid.CreateVersion7(), ProductType.Course, "course", "دورة", "Course", published, price is null ? null : new Money(price.Value), null, offer, null);

    public static CartProduct Book(CartFormat? format, bool published = true, CartOffer? offer = null) =>
        new(Guid.CreateVersion7(), ProductType.Book, "book", "كتاب", null, published, null, format, offer, null);

    public static CartFormat Print(int available = 5, bool active = true, decimal price = 80m) =>
        new(Guid.CreateVersion7(), BookFormatKind.Print, new Money(price), active, available, HasFile: false);

    public static CartFormat Digital(bool hasFile = true, bool active = true) =>
        new(Guid.CreateVersion7(), BookFormatKind.Digital, new Money(40m), active, 0, hasFile);

    public static CartOffer Offer(bool open = true, bool partial = true, bool zakat = false) =>
        new(Guid.CreateVersion7(), Seat, partial, zakat, open, "مقعد", "seat");

    public static CartProduct Sponsorable(CartOffer? offer = null) => Course(offer: offer ?? Offer());

    public static CartCause CauseFacts(decimal? unit = 1_500m, bool custom = true, decimal min = 10m, bool zakat = true, bool open = true) =>
        new(unit is null ? null : new Money(unit.Value), unit is null ? null : "مبرد", unit is null ? null : "cooler", custom, new Money(min), zakat, open);

    public static CartProduct Cause(CartCause? cause = null, bool published = true) =>
        new(Guid.CreateVersion7(), ProductType.Cause, "cause", "سقيا", null, published, null, null, null, cause ?? CauseFacts());

    public static CartLineSpec Buy(CartProduct p, int quantity = 1) =>
        new(p.ProductId, p.Format?.Id, LineKind.Buy, LineMode.Units, quantity, null, null, null);

    public static CartLineSpec Sponsor(CartProduct p, int quantity = 1, GiftIntent? intent = null, CartDedication? dedication = null) =>
        new(p.ProductId, null, LineKind.Sponsor, LineMode.Units, quantity, null, intent, dedication);

    public static CartLineSpec SponsorAmount(CartProduct p, decimal? amount, GiftIntent? intent = null) =>
        new(p.ProductId, null, LineKind.Sponsor, LineMode.Amount, 1, amount is null ? null : new Money(amount.Value), intent, null);

    public static CartLineSpec Donate(CartProduct p, int quantity = 1, GiftIntent? intent = null, CartDedication? dedication = null) =>
        new(p.ProductId, null, LineKind.Donate, LineMode.Units, quantity, null, intent, dedication);

    public static CartLineSpec DonateAmount(CartProduct p, decimal? amount, GiftIntent? intent = null) =>
        new(p.ProductId, null, LineKind.Donate, LineMode.Amount, 1, amount is null ? null : new Money(amount.Value), intent, null);

    public static CartDedication Dedication(string toName = "نورة", string toMobile = "+966551234567", string? fromName = "أحمد",
        string? templateKey = "eid", string? text = null) =>
        new(toName, toMobile, fromName, templateKey, text);

    public static CustomerOwnership Owning(params Guid[] courses) =>
        new(new HashSet<Guid>(courses), new Dictionary<Guid, string>());

    public static CustomerOwnership Unpaid(Guid course, string orderRef) =>
        new(new HashSet<Guid>(), new Dictionary<Guid, string> { [course] = orderRef });
}
