using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Domain.IRepositories;

public interface ICustomerRepository : IGenericRepository<Customer>
{
    /// Exact E.164 match (`customer.mobile` is unique).
    Task<Customer?> FindByMobileAsync(string mobileE164, CancellationToken ct = default);

    /// R12-3: locks the donor row `FOR UPDATE` (no row version change) and reloads it if tracked. Every use case that
    /// touches a donor's cart takes it first — customer, then cart — so a deletion and a cart write serialise. Call inside
    /// a transaction.
    Task LockForUpdateAsync(Guid customerId, CancellationToken ct = default);
}
