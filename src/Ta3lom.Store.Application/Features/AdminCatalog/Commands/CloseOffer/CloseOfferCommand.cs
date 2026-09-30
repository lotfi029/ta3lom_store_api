using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.CloseOffer;

/// `POST /v1/admin/offers/{offerId}/close` (#71): `is_active = false`. The pool sweep (RUL-46, `sweepPool`) is
/// added by S-42; until then the response carries `swept: null`.
public sealed record CloseOfferCommand(Guid OfferId, bool SweepPool) : ICommand<CloseOfferResponse>;

internal sealed class CloseOfferCommandHandler(
    IGenericRepository<SponsorshipOffer> offers,
    IProductRepository products,
    AdminCatalogReader reader,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<CloseOfferCommand, CloseOfferResponse>
{
    public async Task<Result<CloseOfferResponse>> HandleAsync(CloseOfferCommand command, CancellationToken ct = default)
    {
        var offer = await offers.GetByIdAsync(command.OfferId, ct);
        if (offer is null)
        {
            return GeneralErrors.NotFound;
        }

        offer.Close();
        (await products.GetByIdAsync(offer.ProductId, ct))?.Touch(clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return new CloseOfferResponse(await reader.OfferAsync(offer, ct), null, null);
    }
}
