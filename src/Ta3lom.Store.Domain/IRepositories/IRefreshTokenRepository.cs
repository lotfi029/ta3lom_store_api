using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Domain.IRepositories;

public interface IRefreshTokenRepository : IGenericRepository<RefreshToken>
{
    /// Locked `FOR UPDATE`: two refreshes of the same token serialise (09 §5.8).
    Task<RefreshToken?> FindByHashForUpdateAsync(string tokenHash, CancellationToken ct = default);

    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);

    /// Reuse detection: every live token of the family is revoked.
    Task<int> RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct = default);

    /// "Sign out everywhere" (FR-S28) and staff offboarding.
    Task<int> RevokeAllForSubjectAsync(TokenSubjectType subjectType, Guid subjectId, DateTimeOffset now, CancellationToken ct = default);
}
