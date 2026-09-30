using Ta3lom.Store.Application.DTOs.AdminCatalog;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Application.Features.AdminCatalog.Commands.CloseOffer;

/// `POST /v1/admin/offers/{offerId}/close` (#71): `is_active = false`; with `sweepPool` the non-zakat pool remainder moves to
/// the general fund in the same transaction (RUL-46, `PoolSweeper`; `swept`/`pairId` in the response, null when nothing moved).
public sealed record CloseOfferCommand(Guid OfferId, bool SweepPool) : ICommand<CloseOfferResponse>;

internal sealed class CloseOfferCommandHandler(
    IGenericRepository<SponsorshipOffer> offers,
    IProductRepository products,
    AdminCatalogReader reader,
    Giving.PoolSweeper sweeper,
    ICurrentActor actor,
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

        var swept = await uow.ExecuteInTransactionAsync(async token =>
        {
            var now = clock.UtcNow;
            offer.Close();
            (await products.GetByIdAsync(offer.ProductId, token))?.Touch(now);
            var result = command.SweepPool ? await sweeper.SweepAsync(offer.Id, offer.ProductId, actor.Name ?? "staff", now, token) : null;
            await uow.SaveChangesAsync(token);
            return Result.Success(result);
        }, ct);
        return new CloseOfferResponse(await reader.OfferAsync(offer, ct), swept.Value?.Amount.ToWire(), swept.Value?.PairId);
    }
}
