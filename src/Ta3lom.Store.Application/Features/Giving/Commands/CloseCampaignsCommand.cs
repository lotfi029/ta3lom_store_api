using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Domain.Entities.Catalog;

namespace Ta3lom.Store.Application.Features.Giving.Commands;

/// `CloseCampaigns` job (00:05 Riyadh, 09 §6, database/03 §5; RUL-09, RUL-46): every sponsorship offer past `ends_at` is
/// deactivated and its non-zakat pool remainder swept to the general fund (also offers closed earlier without a sweep).
/// One transaction per offer; a second run finds nothing to do. Returns the number of offers closed or swept.
public sealed record CloseCampaignsCommand : ICommand<int>;

internal sealed class CloseCampaignsCommandHandler(IApplicationDbContext db, IGenericRepository<SponsorshipOffer> offers, PoolSweeper sweeper,
    IUnitOfWork uow, IClock clock) : ICommandHandler<CloseCampaignsCommand, int>
{
    public const string Actor = "job:CloseCampaigns";

    public async Task<Result<int>> HandleAsync(CloseCampaignsCommand command, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var due = await (from o in db.SponsorshipOffers.AsNoTracking()
                         join p in db.SponsorshipPools.AsNoTracking() on o.Id equals p.SponsorshipOfferId into pools
                         from p in pools.DefaultIfEmpty()
                         where o.EndsAt <= now && (o.IsActive || (p != null && p.Balance > Money.Zero))
                         select o.Id).ToListAsync(ct);
        var changed = 0;
        foreach (var offerId in due)
        {
            var did = await uow.ExecuteInTransactionAsync(async token =>
            {
                var offer = await offers.GetByIdAsync(offerId, token);
                if (offer is null)
                {
                    return Result.Success(false);
                }

                var wasActive = offer.IsActive;
                offer.Close();
                var swept = await sweeper.SweepAsync(offer.Id, offer.ProductId, Actor, clock.UtcNow, token);
                await uow.SaveChangesAsync(token);
                return Result.Success(wasActive || swept is not null);
            }, ct);
            changed += did is { IsSuccess: true, Value: true } ? 1 : 0;
        }

        return changed;
    }
}
