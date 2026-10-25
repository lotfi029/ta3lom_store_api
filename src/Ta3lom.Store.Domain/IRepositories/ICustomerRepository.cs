using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Domain.IRepositories;

public interface ICustomerRepository : IGenericRepository<Customer>
{
    /// Exact E.164 match (`customer.mobile` is unique).
    Task<Customer?> FindByMobileAsync(string mobileE164, CancellationToken ct = default);
}
