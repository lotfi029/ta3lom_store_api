using Ta3lom.Store.Application.Features.Settings;

namespace Ta3lom.Store.Application.Interfaces;

/// Cached typed settings (09 §4.10): 60-second cache, invalidated on update.
public interface IStoreSettings
{
    Task<StoreSettings> GetAsync(CancellationToken ct = default);

    void Invalidate();
}
