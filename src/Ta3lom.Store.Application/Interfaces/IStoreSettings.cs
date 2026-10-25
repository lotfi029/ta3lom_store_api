using Ta3lom.Store.Application.Features.Settings;

namespace Ta3lom.Store.Application.Interfaces;

/// Cached typed settings: 60-second cache per process, invalidated on update.
public interface IStoreSettings
{
    Task<StoreSettings> GetAsync(CancellationToken ct = default);

    /// Read from the database now, bypassing (and not filling) the cache: for checks that must see committed values.
    Task<StoreSettings> GetFreshAsync(CancellationToken ct = default);

    void Invalidate();
}
