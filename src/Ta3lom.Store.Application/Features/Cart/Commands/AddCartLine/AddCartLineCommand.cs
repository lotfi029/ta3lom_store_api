using System.Globalization;
using FluentValidation;
using Ta3lom.Store.Application.DTOs.Cart;
using Ta3lom.Store.Domain.Entities.Carts;

namespace Ta3lom.Store.Application.Features.Cart.Commands.AddCartLine;

/// `POST /v1/cart/lines` (#12, RUL-12 … RUL-21): creates the cart on first use; the endpoint sets `t3s_cart` for guests.
public sealed record AddCartLineCommand(Guid? GuestCartId, AddCartLineRequest Request) : ICommand<CartResponse>;

internal sealed class AddCartLineCommandHandler(CartAccess access, CartCatalog catalog, IUnitOfWork uow, IClock clock)
    : ICommandHandler<AddCartLineCommand, CartResponse>
{
    public async Task<Result<CartResponse>> HandleAsync(AddCartLineCommand command, CancellationToken ct = default)
    {
        var r = command.Request;
        var spec = new CartLineSpec(r.ProductId, r.BookFormatId, EnumWire.Parse<LineKind>(r.Kind), EnumWire.Parse<LineMode>(r.Mode), r.Quantity,
            r.Amount is null ? null : Money.Round(decimal.Parse(r.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)),
            r.Intent is null ? null : EnumWire.Parse<GiftIntent>(r.Intent),
            r.Dedication is { } d ? Dedication(d) : null);

        var added = await uow.ExecuteInTransactionAsync(async token =>
        {
            var cart = await access.ForWriteAsync(command.GuestCartId, token);
            var snapshots = await catalog.SnapshotsAsync([(spec.ProductId, spec.BookFormatId)], token);
            if (!snapshots.TryGetValue((spec.ProductId, spec.BookFormatId), out var product))
            {
                return Result.Failure<Guid>(CartErrors.ProductUnavailable);
            }

            var owned = await catalog.OwnershipAsync(access.CustomerId, token);
            var line = cart.AddLine(spec, product, owned, await catalog.LimitsAsync(token), clock.UtcNow);
            return line.IsFailure ? Result.Failure<Guid>(line.Error) : Result.Success(cart.Id);
        }, ct);
        if (added.IsFailure)
        {
            return added.Error;
        }

        return await catalog.RenderAsync(await access.ForReadAsync(added.Value, ct), access.CustomerId, [], ct);
    }

    /// The mobile is kept as E.164 (RUL-21); an invalid one fails in `CartLineRules` with `cart.dedication_invalid`.
    private static CartDedication Dedication(DedicationInput d) =>
        new(d.ToName.Trim(), SaudiMobile.TryNormalize(d.ToMobile, out var e164) ? e164 : d.ToMobile, d.FromName?.Trim(),
            d.TemplateKey, string.IsNullOrWhiteSpace(d.Text) ? null : d.Text.Trim());
}

internal sealed class AddCartLineRequestValidator : AbstractValidator<AddCartLineRequest>
{
    public AddCartLineRequestValidator()
    {
        RuleFor(r => r.ProductId).NotEmpty();
        RuleFor(r => r.Kind).Must(k => EnumWire.TryParse<LineKind>(k, out _)).WithMessage("kind must be buy, sponsor or donate.");
        RuleFor(r => r.Mode).Must(m => EnumWire.TryParse<LineMode>(m, out _)).WithMessage("mode must be units or amount.");
        RuleFor(r => r.Quantity).InclusiveBetween(1, CartLineRules.GiftMaxQuantity);
        RuleFor(r => r.Quantity).Equal(1).When(r => r.Mode == "amount").WithMessage("quantity must be 1 for an amount line.");
        RuleFor(r => r.Amount).NotNull().Matches(@"^\d{1,10}\.\d{2}$").When(r => r.Mode == "amount")
            .WithMessage("amount must be a money string such as \"100.00\" for an amount line.");
        RuleFor(r => r.Amount).Null().When(r => r.Mode == "units").WithMessage("amount is only allowed on amount lines.");
        RuleFor(r => r.Intent).Must(i => i is null || EnumWire.TryParse<GiftIntent>(i, out _)).WithMessage("intent must be sadaqa or zakat.");
        RuleFor(r => r.Intent).Null().When(r => r.Kind == "buy").WithMessage("Purchases have no intent (RUL-19).");
        RuleFor(r => r.BookFormatId).Null().When(r => r.Kind != "buy").WithMessage("bookFormatId is only used on purchases.");
        RuleFor(r => r.Dedication!.ToName).NotEmpty().MaximumLength(60).When(r => r.Dedication is not null);
        RuleFor(r => r.Dedication!.ToMobile).NotEmpty().MaximumLength(20).When(r => r.Dedication is not null);
        RuleFor(r => r.Dedication!.FromName).MaximumLength(60).When(r => r.Dedication is not null);
        RuleFor(r => r.Dedication!.TemplateKey).MaximumLength(40).When(r => r.Dedication is not null);
        RuleFor(r => r.Dedication!.Text).MaximumLength(140).When(r => r.Dedication is not null);
    }
}

internal sealed class AddCartLineCommandValidator : AbstractValidator<AddCartLineCommand>
{
    public AddCartLineCommandValidator() => RuleFor(c => c.Request).NotNull().SetValidator(new AddCartLineRequestValidator());
}
