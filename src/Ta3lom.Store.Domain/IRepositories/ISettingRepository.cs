using Ta3lom.Store.Domain.Entities.Settings;

namespace Ta3lom.Store.Domain.IRepositories;

public interface ISettingRepository : IGenericRepository<Setting>
{
    /// L-7: transaction-scoped lock on the settings, so a setting update is checked against the others one at a time.
    Task LockAsync(CancellationToken ct = default);
}
