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
}
