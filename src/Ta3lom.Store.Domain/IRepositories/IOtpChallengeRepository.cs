using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Domain.IRepositories;

public interface IOtpChallengeRepository : IGenericRepository<OtpChallenge>
{
    /// Transaction-scoped advisory lock on the mobile (D-10): parallel requests for one number are counted one after the other.
    Task LockMobileAsync(string mobileE164, CancellationToken ct = default);

    /// Transaction-scoped advisory lock on the client IP hash (L-3): parallel requests for different numbers from one IP
    /// are counted one after the other. Always taken before the mobile lock (fixed order, no deadlock).
    Task LockIpAsync(string ipHash, CancellationToken ct = default);

    /// Codes issued to <paramref name="mobileE164"/> since <paramref name="since"/>, oldest first (D-10: 3 per 15 min).
    Task<IReadOnlyList<DateTimeOffset>> IssuedForMobileSinceAsync(string mobileE164, DateTimeOffset since, CancellationToken ct = default);

    /// Codes requested from one hashed IP since <paramref name="since"/>, oldest first (D-10: 10 per hour).
    Task<IReadOnlyList<DateTimeOffset>> IssuedForIpSinceAsync(string ipHash, DateTimeOffset since, CancellationToken ct = default);

    /// All codes issued since <paramref name="since"/> (the daily global cap; rows live 24 h).
    Task<int> CountIssuedSinceAsync(DateTimeOffset since, CancellationToken ct = default);

    /// The newest unconsumed challenge, locked `FOR UPDATE` so parallel verifies count attempts correctly.
    Task<OtpChallenge?> LatestOpenForUpdateAsync(string mobileE164, OtpPurpose purpose, CancellationToken ct = default);
}
