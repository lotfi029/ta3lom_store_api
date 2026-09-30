using FluentValidation;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Domain.Entities.Catalog;
using Ta3lom.Store.Domain.Entities.Giving;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.CreateOffer;

/// `POST /v1/admin/products/{id}/offers` (#69, RUL-06): courses and books only, one active offer per product
/// (`ux_offer_one_active`), with its `sponsorship_pool` row. Zakat on offers is S2 → rejected in S1.
public sealed record CreateOfferCommand(Guid ProductId, OfferInput Input) : ICommand<OfferResponse>;

internal sealed class CreateOfferCommandHandler(
    IProductRepository products,
    IGenericRepository<SponsorshipOffer> offers,
    IGenericRepository<SponsorshipPool> pools,
    AdminCatalogReader reader,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<CreateOfferCommand, OfferResponse>
{
    public async Task<Result<OfferResponse>> HandleAsync(CreateOfferCommand command, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(command.ProductId, ct);
        if (product is null)
        {
            return AdminCatalogErrors.ProductNotFound;
        }

        if (product.Type == ProductType.Cause)
        {
            return Error.Validation(new Dictionary<string, string[]> { ["type"] = ["Only courses and books can be sponsored (RUL-06)."] });
        }

        if (await offers.ExistsAsync(o => o.ProductId == product.Id && o.IsActive, ct))
        {
            return AdminCatalogErrors.OfferActiveExists;
        }

        var now = clock.UtcNow;
        var i = command.Input;
        var startsAt = i.StartsAtLocal is { } s ? AdminInput.FromRiyadh(s) : now;
        var endsAt = AdminInput.FromRiyadh(i.EndsAtLocal);
        if (endsAt <= startsAt)
        {
            return AdminCatalogErrors.OfferDatesInvalid;
        }

        var offer = SponsorshipOffer.Create(product.Id, AdminInput.Money(i.SeatPrice), i.UnitLabelAr ?? "مقعد", i.UnitLabelEn ?? "seat", i.TargetUnits,
            i.BeneficiaryGroup, i.GroupLabelAr, i.GroupLabelEn, i.AllowPartial, i.CloseOnTarget, startsAt, endsAt, now);
        offers.Add(offer);
        pools.Add(new SponsorshipPool(offer.Id, now));
        product.Touch(now);
        await uow.SaveChangesAsync(ct);
        return await reader.OfferAsync(offer, ct);
    }
}

internal sealed class OfferInputValidator : AbstractValidator<OfferInput>
{
    public OfferInputValidator()
    {
        RuleFor(i => i.SeatPrice).Must(p => AdminInput.IsMoney(p) && AdminInput.Decimal(p) > 0).WithMessage("seatPrice must be a positive money string.");
        RuleFor(i => i.TargetUnits).GreaterThan(0);
        RuleFor(i => i.BeneficiaryGroup).NotEmpty().Matches("^[a-z0-9_]{2,60}$").WithMessage("beneficiaryGroup must be a machine key (a-z, 0-9, _).");
        RuleFor(i => i.GroupLabelAr).NotEmpty();
        RuleFor(i => i.ZakatEligible).Equal(false).WithMessage("Zakat on sponsorship offers is not available in S1 (RUL-19).");
    }
}

internal sealed class CreateOfferCommandValidator : AbstractValidator<CreateOfferCommand>
{
    public CreateOfferCommandValidator() => RuleFor(c => c.Input).NotNull().SetValidator(new OfferInputValidator());
}
