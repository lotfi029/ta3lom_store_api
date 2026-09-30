using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Domain.IRepositories;

public interface IOtpChallengeRepository : IGenericRepository<OtpChallenge>
{
    /// Codes issued to <paramref name="mobileE164"/> since <paramref name="since"/>, oldest first (RUL-60: 3 per 15 min).
    Task<IReadOnlyList<DateTimeOffset>> IssuedForMobileSinceAsync(string mobileE164, DateTimeOffset since, CancellationToken ct = default);

    /// Codes requested from one hashed IP since <paramref name="since"/>, oldest first (RUL-60: 10 per hour).
    Task<IReadOnlyList<DateTimeOffset>> IssuedForIpSinceAsync(string ipHash, DateTimeOffset since, CancellationToken ct = default);

    /// All codes issued since <paramref name="since"/> (08 §6.2 daily global cap; rows live 24 h).
    Task<int> CountIssuedSinceAsync(DateTimeOffset since, CancellationToken ct = default);

    /// The newest unconsumed challenge, locked `FOR UPDATE` so parallel verifies count attempts correctly.
    Task<OtpChallenge?> LatestOpenForUpdateAsync(string mobileE164, OtpPurpose purpose, CancellationToken ct = default);

    /// PDPL: codes older than <paramref name="before"/> are deleted (PruneOtp, hourly).
    Task<int> DeleteCreatedBeforeAsync(DateTimeOffset before, CancellationToken ct = default);
}
