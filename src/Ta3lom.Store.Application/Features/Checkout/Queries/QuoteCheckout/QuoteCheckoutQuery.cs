using Ta3lom.Store.Application.DTOs.Checkout;
using Ta3lom.Store.Application.Features.Cart;

namespace Ta3lom.Store.Application.Features.Checkout.Queries.QuoteCheckout;

/// `POST /v1/checkout/quote` (#40, FR-S31): reprices and re-validates the current cart without writing.
public sealed record QuoteCheckoutQuery(Guid? GuestCartId, Guid? ShippingAddressId) : IQuery<QuoteResponse>;

internal sealed class QuoteCheckoutQueryHandler(CartAccess access, CheckoutPricing pricing) : IQueryHandler<QuoteCheckoutQuery, QuoteResponse>
{
    public async Task<Result<QuoteResponse>> HandleAsync(QuoteCheckoutQuery query, CancellationToken ct = default)
    {
        var cart = await access.ForReadAsync(query.GuestCartId, ct);
        if (cart is null || cart.Lines.Count == 0)
        {
            return CheckoutErrors.CartEmpty;
        }

        return (await pricing.PriceAsync(cart, access.CustomerId, ct)).Quote;
    }
}
