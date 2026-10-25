using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ta3lom.Store.Application.Abstractions.Time;
using Ta3lom.Store.Application.Features.Settings;
using Ta3lom.Store.Application.Interfaces;
using Ta3lom.Store.Domain.Constants;
using Ta3lom.Store.Domain.ValueObjects;
using Ta3lom.Store.Infrastructure.Persistence;

namespace Ta3lom.Store.Infrastructure.Services.Settings;

/// Typed settings read from `setting.value` (jsonb), cached for 60 s per process and invalidated on update. Missing or
/// unreadable keys fall back to <see cref="StoreSettings.Defaults"/> (the SeedRunner inserts the same values on start).
internal sealed class StoreSettingsProvider(IServiceScopeFactory scopes, IClock clock) : IStoreSettings, IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (StoreSettings Value, DateTimeOffset LoadedAt)? _cache;
    private int _generation;                                     // L-7: a load that overlaps an Invalidate is not cached

    public async Task<StoreSettings> GetAsync(CancellationToken ct = default)
    {
        if (_cache is { } hit && clock.UtcNow - hit.LoadedAt < Lifetime)
        {
            return hit.Value;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_cache is { } again && clock.UtcNow - again.LoadedAt < Lifetime)
            {
                return again.Value;
            }

            var generation = Volatile.Read(ref _generation);
            var value = await LoadAsync(ct);
            if (generation == Volatile.Read(ref _generation))
            {
                _cache = (value, clock.UtcNow);
            }

            return value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<StoreSettings> GetFreshAsync(CancellationToken ct = default) => LoadAsync(ct);

    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        _cache = null;
    }

    private async Task<StoreSettings> LoadAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        var rows = await db.Settings.AsNoTracking().Select(s => new { s.Key, s.Value }).ToListAsync(ct);
        var raw = rows.ToDictionary(r => r.Key, r => JsonDocument.Parse(r.Value).RootElement.Clone(), StringComparer.Ordinal);
        return Parse(raw);
    }

    public void Dispose() => _gate.Dispose();

    public static StoreSettings Parse(IReadOnlyDictionary<string, JsonElement> raw)
    {
        var d = StoreSettings.Defaults;
        return new StoreSettings(
            MoneyOr(raw, SettingKeys.MinGiftAmount, d.MinGiftAmount),
            MoneyOr(raw, SettingKeys.MaxLineAmount, d.MaxLineAmount),
            IntOr(raw, SettingKeys.CartMaxLines, d.CartMaxLines),
            MoneyOr(raw, SettingKeys.CartMaxTotal, d.CartMaxTotal),
            IntOr(raw, SettingKeys.PaymentWindowMinutes, d.PaymentWindowMinutes),
            raw.TryGetValue(SettingKeys.AmountPresets, out var presets) && presets.ValueKind == JsonValueKind.Array
                ? [.. presets.EnumerateArray().Select(ToMoney).OfType<Money>()]
                : d.AmountPresets,
            Text(raw, SettingKeys.SiteNameAr),
            Text(raw, SettingKeys.SiteNameEn),
            Text(raw, SettingKeys.SupportMobile));
    }

    private static Money? ToMoney(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number when e.TryGetDecimal(out var n) => Money.Round(n),
        JsonValueKind.String when decimal.TryParse(e.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var s) => Money.Round(s),
        _ => null,
    };

    private static Money MoneyOr(IReadOnlyDictionary<string, JsonElement> raw, string key, Money fallback) =>
        raw.TryGetValue(key, out var e) && ToMoney(e) is { } m ? m : fallback;

    private static int IntOr(IReadOnlyDictionary<string, JsonElement> raw, string key, int fallback) =>
        raw.TryGetValue(key, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n : fallback;

    private static string? Text(IReadOnlyDictionary<string, JsonElement> raw, string key) =>
        raw.TryGetValue(key, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
