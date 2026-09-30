using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Domain.IRepositories;

public interface IStaffUserRepository : IGenericRepository<StaffUser>
{
    /// Case-insensitive (`staff_user.email` is citext).
    Task<StaffUser?> FindByEmailAsync(string email, CancellationToken ct = default);
}
