using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Domain.IRepositories;

public interface IStaffUserRepository : IGenericRepository<StaffUser>
{
    /// Case-insensitive (`staff_user.email` is citext).
    Task<StaffUser?> FindByEmailAsync(string email, CancellationToken ct = default);

    /// Locks the member's row `FOR UPDATE` and reloads the tracked entity, so a failure count or a TOTP step is applied to
    /// the current row (A-1: parallel attempts serialise). Call inside a transaction.
    Task LockForUpdateAsync(StaffUser user, CancellationToken ct = default);

    /// The active admins, locked `FOR UPDATE` (D-12 last-admin guard): two admins demoting each other serialise.
    Task<IReadOnlyList<Guid>> LockActiveAdminIdsAsync(CancellationToken ct = default);
}
