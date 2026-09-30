using Ta3lom.Store.Application.DTOs.Checkout;
using Ta3lom.Store.Application.Features.Cart;
using Ta3lom.Store.Domain.Entities.Carts;
using Ta3lom.Store.Domain.Entities.Ordering;
using Ta3lom.Store.Domain.Services;
using CartEntity = Ta3lom.Store.Domain.Entities.Carts.Cart;

namespace Ta3lom.Store.Application.Features.Checkout;

/// The cart repriced and re-validated from the catalogue (database/01 §4.2, RUL-15, RUL-24): the quote the customer sees
/// and, when valid, the order line drafts checkout snapshots.
internal sealed record PricedCart(QuoteResponse Quote, IReadOnlyList<OrderLineDraft> Drafts, Money GrandTotal, OrderShipping Shipping,
    bool RequiresShipping, IReadOnlyList<(Guid BookFormatId, int Quantity)> PrintReservations);

internal sealed class CheckoutPricing(CartCatalog catalog, IStoreSettings settings, IClock clock)
{
    public async Task<PricedCart> PriceAsync(CartEntity cart, Guid? customerId, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var limits = await catalog.LimitsAsync(ct);
        var owned = await catalog.OwnershipAsync(customerId, ct);
        var snapshots = await catalog.SnapshotsAsync(cart.Lines.Select(l => (l.ProductId, l.BookFormatId)), ct);
        var now = clock.UtcNow;

        List<QuoteLine> quoteLines = [];
        List<OrderLineDraft> drafts = [];
        List<(Guid, int)> reservations = [];
        var requiresShipping = false;
        var changed = false;
        foreach (var line in cart.Lines.OrderBy(l => l.AddedAt))
        {
            snapshots.TryGetValue((line.ProductId, line.BookFormatId), out var p);
            var spec = new CartLineSpec(line.ProductId, line.BookFormatId, line.Kind, line.Mode, line.Quantity,
                line.Mode == LineMode.Amount ? line.Amount : null, line.Intent, line.Dedication);
            var error = p is null ? CartErrors.ProductUnavailable : CartLineRules.Check(spec, p, limits, now) ?? Ownership(spec, p, owned);
            if (error is not null || p is null)
            {
                quoteLines.Add(new QuoteLine(line.Id, "invalid", error?.Code, null, line.UnitPrice.ToWire(), line.Amount.ToWire(), Money.Zero.ToWire()));
                continue;
            }

            var unitPrice = CartLineRules.UnitPrice(spec, p);                           // RUL-15: the catalogue price wins
            var quantity = CartLineRules.NormaliseQuantity(spec, p);
            var amount = line.Mode == LineMode.Amount ? line.Amount : unitPrice * quantity;
            var vatRate = line.Kind == LineKind.Buy && s.VatEnabled ? s.VatRate : 0m;   // RUL-27: gifts carry no VAT
            var vat = VatCalculator.Included(amount, vatRate);
            var lineChanged = unitPrice != line.UnitPrice;
            changed |= lineChanged;
            quoteLines.Add(new QuoteLine(line.Id, lineChanged ? "changed" : "ok", null, lineChanged ? line.UnitPrice.ToWire() : null,
                unitPrice.ToWire(), amount.ToWire(), vat.ToWire()));

            if (line.Kind == LineKind.Buy && p.Format is { Kind: BookFormatKind.Print } print)
            {
                requiresShipping = true;
                reservations.Add((print.Id, quantity));
            }

            drafts.Add(new OrderLineDraft(p.ProductId, p.Type, line.Kind == LineKind.Buy ? p.Format?.Id : null,
                line.Kind == LineKind.Sponsor ? p.Offer?.Id : null, line.Kind, line.Mode, quantity, unitPrice, amount, vatRate, vat,
                line.Kind == LineKind.Buy ? null : line.Intent ?? GiftIntent.Sadaqa, p.TitleAr, p.TitleEn,
                line.Kind == LineKind.Buy ? null : line.Dedication));
        }

        var purchases = drafts.Where(d => d.Kind == LineKind.Buy).Select(d => d.Amount).Sum();
        var gifts = drafts.Where(d => d.Kind != LineKind.Buy).Select(d => d.Amount).Sum();
        var shippingFee = requiresShipping ? s.ShippingFlatFee : Money.Zero;           // RUL-26
        var shippingVat = VatCalculator.Included(shippingFee, s.VatEnabled ? s.VatRate : 0m);
        var vatTotal = drafts.Select(d => d.VatAmount).Sum() + shippingVat;
        var grand = purchases + shippingFee + gifts;                                    // RUL-31

        List<string> cartIssues = [];
        if (cart.Lines.Count > CartLimits.MaxLines)
        {
            cartIssues.Add(ErrorCodes.CartTooManyLines);
        }

        if (grand > limits.CartMaxTotal)
        {
            cartIssues.Add(ErrorCodes.CartTotalExceeded);
        }

        var valid = quoteLines.All(l => l.Status != "invalid") && cartIssues.Count == 0 && cart.Lines.Count > 0;
        var quote = new QuoteResponse(quoteLines, purchases.ToWire(), shippingFee.ToWire(), gifts.ToWire(), vatTotal.ToWire(), grand.ToWire(),
            changed, requiresShipping, gifts.IsPositive, valid, cartIssues);
        return new PricedCart(quote, drafts, grand, new OrderShipping(shippingFee, shippingVat, null), requiresShipping, reservations);
    }

    /// RUL-13 at checkout: an owned course, or one in another unpaid order, fails the line.
    private static Error? Ownership(CartLineSpec s, CartProduct p, CustomerOwnership owned)
    {
        if (s.Kind != LineKind.Buy || p.Type != ProductType.Course)
        {
            return null;
        }

        return owned.Owns(p.ProductId) ? CartErrors.CourseOwned
            : owned.UnpaidOrderFor(p.ProductId) is { } orderRef ? CartErrors.CourseInUnpaidOrder(orderRef)
            : null;
    }
}
