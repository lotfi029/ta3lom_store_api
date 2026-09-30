using FluentValidation;
using Ta3lom.Store.Application.DTOs.Checkout;
using Ta3lom.Store.Application.Features.Settings;
using Ta3lom.Store.Domain.Entities.Identity;
using Ta3lom.Store.Domain.Entities.Ordering;

namespace Ta3lom.Store.Application.Features.Checkout.Commands.PlaceOrder;

/// `POST /v1/checkout` (#41, database/01 §4.3): one transaction re-validates the cart (RUL-24), checks the expected
/// total (RUL-15), reserves print stock (RUL-30), allocates `T3-YY-NNNNNN` (RUL-28), snapshots lines and dedications
/// and converts the cart. The payment session starts after commit (doc 15 §3; `NullPaymentInitiator` here).
public sealed record PlaceOrderCommand(CheckoutRequest Request) : ICommand<CheckoutResponse>;

internal sealed class PlaceOrderCommandHandler(
    ICurrentActor actor,
    ICustomerRepository customers,
    ICartRepository carts,
    IOrderRepository orders,
    IStockRepository stock,
    IGenericRepository<Dedication> dedications,
    IGenericRepository<CustomerAddress> addresses,
    CheckoutPricing pricing,
    IStoreSettings settings,
    IOrderStatusTokens statusTokens,
    IPaymentInitiator payments,
    IWebLinks links,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<PlaceOrderCommand, CheckoutResponse>
{
    public async Task<Result<CheckoutResponse>> HandleAsync(PlaceOrderCommand command, CancellationToken ct = default)
    {
        if (actor.Kind != ActorKind.Customer || actor.Id is not { } customerId)
        {
            return GeneralErrors.Unauthenticated;
        }

        var r = command.Request;
        if (string.IsNullOrWhiteSpace(r.Contact?.FullName) || string.IsNullOrWhiteSpace(r.Contact?.Email))
        {
            return CheckoutErrors.ContactRequired;                                             // RUL-25
        }

        if (!r.AcceptTerms)
        {
            return CheckoutErrors.TermsNotAccepted;                                            // FR-S30
        }

        var s = await settings.GetAsync(ct);
        var placed = await uow.ExecuteInTransactionAsync(async token => await PlaceAsync(customerId, r, s, token), ct);
        if (placed.IsFailure)
        {
            return placed.Error;
        }

        // After commit: never call the gateway inside the transaction (CLAUDE.md, doc 15 §3).
        var order = placed.Value;
        var statusToken = statusTokens.Issue(order.Id);
        var start = await payments.StartAsync(new OrderPaymentRequest(order.Id, order.Reference, order.GrandTotal.Amount, Money.Currency,
            order.ContactName, order.ContactMobile, order.ContactEmail, order.Locale, links.OrderThanks(order.Locale, order.Reference, statusToken),
            order.GiftsTotal.IsPositive, order.PaymentDueAt), ct);
        if (start.IsFailure)
        {
            return CheckoutErrors.GatewayUnavailable(order.Reference, statusToken);         // order kept pending_payment (RUL-37)
        }

        return new CheckoutResponse(order.Id, order.Reference, order.Status.ToWire(), order.GrandTotal.ToWire(), order.PaymentDueAt, statusToken,
            new CheckoutPayment(null, null, r.PaymentMethod, start.Value.RedirectUrl, start.Value.SdkPayload, start.Value.ExpiresAt));
    }

    private async Task<Result<Order>> PlaceAsync(Guid customerId, CheckoutRequest r, StoreSettings s, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var customer = await customers.GetByIdAsync(customerId, ct);
        if (customer is null)
        {
            return Result.Failure<Order>(GeneralErrors.Unauthenticated);
        }

        if (customer.IsBlocked)
        {
            return Result.Failure<Order>(AuthErrors.AccountBlocked);                          // RUL-61
        }

        var cart = await carts.GetActiveForCustomerForUpdateAsync(customerId, ct);
        if (cart is null || cart.Lines.Count == 0)
        {
            return Result.Failure<Order>(CheckoutErrors.CartEmpty);
        }

        var priced = await pricing.PriceAsync(cart, customerId, ct);
        if (!priced.Quote.Valid)
        {
            return Result.Failure<Order>(CheckoutErrors.LinesInvalid(priced.Quote.Lines.Where(l => l.Status == "invalid")
                .Select(l => new { l.LineId, l.Code }).Concat(priced.Quote.CartIssues.Select(code => new { LineId = Guid.Empty, Code = (string?)code }))
                .ToList()));                                                                   // RUL-24
        }

        if (Money.Parse(r.ExpectedGrandTotal) != priced.GrandTotal)
        {
            return Result.Failure<Order>(CheckoutErrors.PriceChanged(priced.Quote));          // RUL-15
        }

        ShippingAddress? address = null;
        if (priced.RequiresShipping)                                                           // RUL-26: only with a print line
        {
            address = await AddressAsync(customerId, r, now, ct);
            if (address is null)
            {
                return Result.Failure<Order>(CheckoutErrors.AddressRequired);
            }
        }

        var failed = await stock.TryReserveAsync(priced.PrintReservations, ct);              // RUL-30
        if (failed is { } formatId)
        {
            return Result.Failure<Order>(CheckoutErrors.OutOfStock(formatId));
        }

        var reference = Order.FormatReference(clock.RiyadhToday.Year, await orders.NextReferenceNumberAsync(ct));
        var contact = new OrderContact(r.Contact!.FullName!.Trim(), customer.Mobile, r.Contact.Email!.Trim());
        var (order, orderDedications) = Order.Place(reference, customerId, contact, r.Locale ?? customer.Locale, r.IsAnonymous,
            priced.Shipping with { Address = address }, cart.Id, priced.Drafts, now, now.AddMinutes(s.OrderPaymentTimeoutMin));
        orders.Add(order);
        dedications.AddRange(orderDedications);
        cart.MarkConverted(now);

        // RUL-25: the verified customer keeps the name and email typed at checkout when their profile lacks them.
        customer.UpdateProfile(customer.FullName is null ? contact.Name : null, customer.Email is null ? contact.Email : null, null, null, null, now);
        return Result.Success(order);
    }

    private async Task<ShippingAddress?> AddressAsync(Guid customerId, CheckoutRequest r, DateTimeOffset now, CancellationToken ct)
    {
        if (r.ShippingAddress is { } input)
        {
            var address = new ShippingAddress(input.RecipientName.Trim(), SaudiMobile.TryNormalize(input.RecipientMobile, out var e164) ? e164 : input.RecipientMobile,
                input.City.Trim(), input.District, input.ShortAddress, input.Street, input.BuildingNo, input.PostalCode);
            if (r.SaveAddress)
            {
                addresses.Add(CustomerAddress.Create(customerId, address, now));
            }

            return address;
        }

        if (r.ShippingAddressId is { } id)
        {
            var saved = await addresses.FirstOrDefaultAsync(a => a.Id == id && a.CustomerId == customerId, ct);
            return saved?.ToShippingAddress();
        }

        return null;
    }
}

internal sealed class CheckoutRequestValidator : AbstractValidator<CheckoutRequest>
{
    private static readonly string[] Methods = ["mada", "applepay", "card", "stcpay"];

    public CheckoutRequestValidator()
    {
        RuleFor(r => r.PaymentMethod).Must(m => Methods.Contains(m)).WithMessage("paymentMethod must be one of mada, applepay, card, stcpay.");
        RuleFor(r => r.ExpectedGrandTotal).NotEmpty().Matches(@"^\d{1,10}\.\d{2}$").WithMessage("expectedGrandTotal must be a money string such as \"350.00\".");
        RuleFor(r => r.Locale).Must(l => l is null or "ar" or "en").WithMessage("locale must be ar or en.");
        RuleFor(r => r.Contact!.FullName).Length(2, 100).When(r => !string.IsNullOrWhiteSpace(r.Contact?.FullName));
        RuleFor(r => r.Contact!.Email).EmailAddress().MaximumLength(254).When(r => !string.IsNullOrWhiteSpace(r.Contact?.Email));
        When(r => r.ShippingAddress is not null, () =>
        {
            RuleFor(r => r.ShippingAddress!.RecipientName).NotEmpty().Length(2, 100);
            RuleFor(r => r.ShippingAddress!.RecipientMobile).Must(SaudiMobile.IsValid).WithMessage("recipientMobile must be a Saudi mobile (05XXXXXXXX).");
            RuleFor(r => r.ShippingAddress!.City).NotEmpty().MaximumLength(60);
            RuleFor(r => r.ShippingAddress!.ShortAddress).Matches("^[A-Z]{4}[0-9]{4}$").When(r => r.ShippingAddress!.ShortAddress is not null);
            RuleFor(r => r.ShippingAddress!.PostalCode).Matches("^[0-9]{5}$").When(r => r.ShippingAddress!.PostalCode is not null);
        });
    }
}

internal sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator() => RuleFor(c => c.Request).NotNull().SetValidator(new CheckoutRequestValidator());
}

