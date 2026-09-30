using Ta3lom.Store.Domain.Entities.Identity;

namespace Ta3lom.Store.Domain.IRepositories;

public interface ICustomerRepository : IGenericRepository<Customer>
{
    /// Exact E.164 match (`customer.mobile` is unique).
    Task<Customer?> FindByMobileAsync(string mobileE164, CancellationToken ct = default);

    /// RUL-47/52: the customer id for a mobile, creating the row if none exists (`INSERT … ON CONFLICT (mobile) DO NOTHING`,
    /// race-safe), so a sponsored enrolment has an owner before the first login.
    Task<Guid> FindOrCreateByMobileAsync(string mobileE164, DateTimeOffset now, CancellationToken ct = default);
}
