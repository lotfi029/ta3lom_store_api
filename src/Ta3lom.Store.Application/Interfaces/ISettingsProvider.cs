namespace Ta3lom.Store.Application.Interfaces;

/// Typed read of the `setting` table (RUL §M). Implemented with the typed StoreSettings in S-09.
public interface ISettingsProvider
{
    Task<T> GetAsync<T>(string key, CancellationToken ct = default);
}
