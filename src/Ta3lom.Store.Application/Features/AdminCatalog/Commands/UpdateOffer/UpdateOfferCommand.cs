using FluentValidation;
using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Application.Features.AdminCatalog.Commands.CreateOffer;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.UpdateOffer;

/// `PUT /v1/admin/offers/{offerId}` (#70, RUL-06): a seat-price change applies to new cart lines only.
public sealed record UpdateOfferCommand(Guid OfferId, OfferInput Input) : ICommand<OfferResponse>;

internal sealed class UpdateOfferCommandHandler(
    IGenericRepository<SponsorshipOffer> offers,
    IProductRepository products,
    AdminCatalogReader reader,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<UpdateOfferCommand, OfferResponse>
{
    public async Task<Result<OfferResponse>> HandleAsync(UpdateOfferCommand command, CancellationToken ct = default)
    {
        var offer = await offers.GetByIdAsync(command.OfferId, ct);
        if (offer is null)
        {
            return GeneralErrors.NotFound;
        }

        var i = command.Input;
        var startsAt = i.StartsAtLocal is { } s ? AdminInput.FromRiyadh(s) : offer.StartsAt;
        var endsAt = AdminInput.FromRiyadh(i.EndsAtLocal);
        if (endsAt <= startsAt)
        {
            return AdminCatalogErrors.OfferDatesInvalid;
        }

        offer.Update(AdminInput.Money(i.SeatPrice), i.UnitLabelAr ?? offer.UnitLabelAr, i.UnitLabelEn ?? offer.UnitLabelEn, i.TargetUnits, i.BeneficiaryGroup,
            i.GroupLabelAr, i.GroupLabelEn, i.AllowPartial, i.CloseOnTarget, startsAt, endsAt);
        (await products.GetByIdAsync(offer.ProductId, ct))?.Touch(clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return await reader.OfferAsync(offer, ct);
    }
}

internal sealed class UpdateOfferCommandValidator : AbstractValidator<UpdateOfferCommand>
{
    public UpdateOfferCommandValidator() => RuleFor(c => c.Input).NotNull().SetValidator(new OfferInputValidator());
}
