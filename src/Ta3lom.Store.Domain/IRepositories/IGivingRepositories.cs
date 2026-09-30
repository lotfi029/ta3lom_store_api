using Ta3lom.Store.Domain.Entities.Giving;

namespace Ta3lom.Store.Domain.IRepositories;

/// Row locks for pooling (database/01 §4.5, RUL-45). Callers run inside the settlement transaction.
public interface ISponsorshipPoolRepository
{
    /// The offer's pool, locked `FOR UPDATE` (created empty first if the offer has none yet).
    Task<SponsorshipPool> LockAsync(Guid offerId, DateTimeOffset now, CancellationToken ct = default);

    /// Every open (not swept, `remaining > 0`) contribution of the offer, locked, oldest first — plus the ones this unit of
    /// work added and has not saved yet.
    Task<IReadOnlyList<PoolContribution>> LockOpenContributionsAsync(Guid offerId, CancellationToken ct = default);
}

/// Beneficiary register (RUL-51).
public interface IBeneficiaryRepository : IGenericRepository<Beneficiary>
{
    /// `B-000123` from `beneficiary_code_seq`.
    Task<string> NextCodeAsync(CancellationToken ct = default);

    /// The selected beneficiaries, locked `FOR UPDATE` in id order (an allocation batch sees a stable register).
    Task<IReadOnlyList<Beneficiary>> LockManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
}

/// RUL-20: serialises zakat vouchers per product (the product row is locked `FOR UPDATE`) so two vouchers can never
/// overdraw the balance together; returns the balance read under the lock.
public interface IZakatLedgerRepository
{
    Task<Money> LockBalanceAsync(Guid productId, CancellationToken ct = default);
}

/// Unit row locks (database/01 §4.6).
public interface ISponsoredUnitRepository : IGenericRepository<SponsoredUnit>
{
    /// RUL-48: the `count` oldest awaiting units of the product (`funded_at, id`), `FOR UPDATE SKIP LOCKED` — two officers
    /// allocating the same product at once get disjoint slices.
    Task<IReadOnlyList<SponsoredUnit>> LockOldestAwaitingAsync(Guid productId, int count, CancellationToken ct = default);

    /// One unit locked `FOR UPDATE` (completion, lapse).
    Task<SponsoredUnit?> LockAsync(Guid unitId, CancellationToken ct = default);
}
