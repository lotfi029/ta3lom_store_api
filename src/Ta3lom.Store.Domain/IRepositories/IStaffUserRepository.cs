using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Domain.IRepositories;

public interface IStaffUserRepository : IGenericRepository<StaffUser>
{
    /// Case-insensitive (`staff_user.email` is citext).
    Task<StaffUser?> FindByEmailAsync(string email, CancellationToken ct = default);

    /// The active admins, locked `FOR UPDATE` (D-12 last-admin guard): two admins demoting each other serialise.
    Task<IReadOnlyList<Guid>> LockActiveAdminIdsAsync(CancellationToken ct = default);
}
