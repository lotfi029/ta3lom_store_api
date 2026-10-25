using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Donations;
using Ta3lom.Store.Domain.Entities.Donations;

namespace Ta3lom.Store.Application.Features.Donations;

/// `POST /v1/checkout` (BR-A08, D-09, D-13), idempotent per donor and `Idempotency-Key` (endpoint filter). In one
/// transaction under the cart lock: the donor must be active, every line is re-priced from the database (any problem →
/// 422 `checkout.lines_invalid`), the order and its line snapshots are created in `pending_payment` with the reference
/// `T3-YY-NNNNNN` (YY = the Riyadh year), the cart is emptied and `OrderPlaced` is queued in the outbox. The order keeps the
/// `Idempotency-Key`; a retry whose stored response was lost (A-5) gets the order that key placed.
public sealed record CheckoutCommand(Guid CustomerId, CheckoutRequest Request, string? IdempotencyKey = null) : ICommand<CheckoutResponse>;

internal sealed class CheckoutCommandHandler(ICustomerRepository customers, ICartRepository carts, IOrderRepository orders, CartPricer pricer,
    IStoreSettings settings, IOrderStatusTokens statusTokens, IUnitOfWork uow, IClock clock) : ICommandHandler<CheckoutCommand, CheckoutResponse>
{
    public Task<Result<CheckoutResponse>> HandleAsync(CheckoutCommand command, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(token => PlaceAsync(command, token), ct);

    private async Task<Result<CheckoutResponse>> PlaceAsync(CheckoutCommand command, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(command.CustomerId, ct);
        if (customer is not { IsActive: true })
        {
            return AuthErrors.AccountBlocked;
        }

        var cart = await carts.GetForCustomerForUpdateAsync(customer.Id, ct);                // the lock serialises checkouts of the donor
        if (command.IdempotencyKey is { } key && await orders.FindByIdempotencyKeyAsync(customer.Id, key, ct) is { } placed)
        {
            return new CheckoutResponse(placed.Reference, placed.Status.ToWire(), placed.TotalAmount.ToWire(), statusTokens.Create(placed.Reference).Token, null);
        }

        var name = Blank(command.Request.FullName) ?? customer.FullName;
        var email = Blank(command.Request.Email) ?? customer.Email;
        if (name is null || email is null)
        {
            return OrderErrors.ContactRequired;
        }

        if (cart is null || cart.Lines.Count == 0)
        {
            return OrderErrors.CartEmpty;
        }

        var s = await settings.GetAsync(ct);
        var priced = await pricer.PriceAsync(cart.Lines, s, ct);
        if (priced.Problems.Count > 0)
        {
            return OrderErrors.LinesInvalid([.. priced.Problems.Cast<object>()]);
        }

        var now = clock.UtcNow;
        var locale = command.Request.Locale ?? customer.Locale;
        customer.UpdateProfile(name, email, locale, null, now);
        var reference = Order.FormatReference(clock.RiyadhToday.Year, await orders.NextReferenceSequenceAsync(ct));
        var (token, hash) = statusTokens.Create(reference);
        var order = Order.Place(reference, customer.Id, new DonorContact(name, email, customer.Mobile!, locale),
            [.. priced.Lines.Select(l => l.ToDraft())], hash, now.AddMinutes(s.PaymentWindowMinutes), now, command.IdempotencyKey);
        orders.Add(order);
        cart.Clear(now);
        return new CheckoutResponse(order.Reference, order.Status.ToWire(), order.TotalAmount.ToWire(), token, null);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class CheckoutCommandValidator : AbstractValidator<CheckoutCommand>
{
    public CheckoutCommandValidator()
    {
        RuleFor(c => c.Request.FullName).MaximumLength(120);
        RuleFor(c => c.Request.Email).EmailAddress().MaximumLength(254).When(c => !string.IsNullOrWhiteSpace(c.Request.Email));
        RuleFor(c => c.Request.Locale).Must(l => l is null or "ar" or "en").WithMessage("locale must be ar or en.");
        RuleFor(c => c.IdempotencyKey).MaximumLength(64);
    }
}

/// `POST /v1/checkout/quote`: the cart re-priced from the database, with its problems (nothing is written).
public sealed record QuoteCheckoutQuery(CartOwner Owner) : IQuery<CartResponse>;

internal sealed class QuoteCheckoutQueryHandler(CartReader reader, CartPricer pricer, IStoreSettings settings) : IQueryHandler<QuoteCheckoutQuery, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(QuoteCheckoutQuery query, CancellationToken ct = default)
    {
        var cart = await reader.FindAsync(query.Owner, ct);
        if (cart is null || cart.Lines.Count == 0)
        {
            return OrderErrors.CartEmpty;
        }

        return (await pricer.PriceAsync(cart.Lines, await settings.GetAsync(ct), ct)).ToResponse();
    }
}

/// `GET /v1/orders/{reference}/status?t=`: the donor of the order, or whoever holds its status token. Anything else is 404
/// (whether the reference exists is not revealed).
public sealed record GetOrderStatusQuery(string Reference, string? Token, Guid? CustomerId) : IQuery<OrderStatusResponse>;

internal sealed class GetOrderStatusQueryHandler(IApplicationDbContext db, IOrderStatusTokens tokens) : IQueryHandler<GetOrderStatusQuery, OrderStatusResponse>
{
    public async Task<Result<OrderStatusResponse>> HandleAsync(GetOrderStatusQuery query, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Reference == query.Reference, ct);
        var allowed = order is not null
                      && ((query.CustomerId is { } customerId && order.CustomerId == customerId)
                          || (query.Token is { } token && tokens.Matches(token, order.StatusTokenHash)));
        return allowed
            ? new OrderStatusResponse(order!.Reference, order.Status.ToWire(), order.TotalAmount.ToWire(), order.LineCount, order.CreatedAt, order.PaymentDueAt)
            : OrderErrors.NotFound;
    }
}
