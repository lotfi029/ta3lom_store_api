using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Domain.Entities.Giving;
using Ta3lom.Store.Domain.IRepositories;
using Ta3lom.Store.Domain.ValueObjects;

namespace Ta3lom.Store.Infrastructure.Persistence.Repositories;

/// database/01 §4.5: the pool row and its open contributions are locked `FOR UPDATE` in a fixed order (pool, then
/// contributions oldest first), so parallel settlements into the same offer serialise and never lose an update.
internal sealed class SponsorshipPoolRepository(StoreDbContext db) : ISponsorshipPoolRepository
{
    public async Task<SponsorshipPool> LockAsync(Guid offerId, DateTimeOffset now, CancellationToken ct = default)
    {
        var tracked = db.SponsorshipPools.Local.FirstOrDefault(p => p.SponsorshipOfferId == offerId);
        if (tracked is not null)
        {
            return tracked;                                                             // already locked in this unit of work
        }

        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO store.sponsorship_pool (sponsorship_offer_id, balance, updated_at) VALUES ({offerId}, 0, {now})
            ON CONFLICT (sponsorship_offer_id) DO NOTHING
            """, ct);
        return await db.SponsorshipPools
            .FromSql($"SELECT *, xmin FROM store.sponsorship_pool WHERE sponsorship_offer_id = {offerId} FOR UPDATE")
            .SingleAsync(ct);
    }

    public async Task<IReadOnlyList<PoolContribution>> LockOpenContributionsAsync(Guid offerId, CancellationToken ct = default)
    {
        var locked = await db.PoolContributions
            .FromSql($"""
                SELECT * FROM store.pool_contribution
                 WHERE sponsorship_offer_id = {offerId} AND remaining > 0 AND swept_to_fund_at IS NULL
                 ORDER BY created_at, id
                 FOR UPDATE
                """)
            .ToListAsync(ct);
        var pending = db.ChangeTracker.Entries<PoolContribution>()
            .Where(e => e.State == EntityState.Added && e.Entity.SponsorshipOfferId == offerId)
            .Select(e => e.Entity);
        return [.. locked.Concat(pending).Where(c => c.Remaining > Money.Zero && c.SweptToFundAt is null)
            .DistinctBy(c => c.Id).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)];
    }
}

internal sealed class BeneficiaryRepository(StoreDbContext db) : GenericRepository<Beneficiary>(db), IBeneficiaryRepository
{
    public async Task<string> NextCodeAsync(CancellationToken ct = default)
    {
        var n = (await Db.Database.SqlQuery<long>($"SELECT nextval('store.beneficiary_code_seq') AS \"Value\"").ToListAsync(ct)).Single();
        return "B-" + n.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<Beneficiary>> LockManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        var array = ids.ToArray();
        return await Set.FromSql($"SELECT * FROM store.beneficiary WHERE id = ANY({array}) ORDER BY id FOR UPDATE").ToListAsync(ct);
    }
}

internal sealed class SponsoredUnitRepository(StoreDbContext db) : GenericRepository<SponsoredUnit>(db), ISponsoredUnitRepository
{
    public async Task<IReadOnlyList<SponsoredUnit>> LockOldestAwaitingAsync(Guid productId, int count, CancellationToken ct = default) =>
        await Set.FromSql($"""
            SELECT *, xmin FROM store.sponsored_unit
             WHERE product_id = {productId} AND status = 'awaiting_allocation'
             ORDER BY funded_at, id
             LIMIT {count}
             FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct);

    public async Task<SponsoredUnit?> LockAsync(Guid unitId, CancellationToken ct = default) =>
        (await Set.FromSql($"SELECT *, xmin FROM store.sponsored_unit WHERE id = {unitId} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
}
