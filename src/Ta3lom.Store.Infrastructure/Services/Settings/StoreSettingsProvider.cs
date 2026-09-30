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

/// Typed settings (09 §4.10) read from `setting.value` (jsonb), cached for 60 s per process and invalidated on
/// update. Missing keys fall back to the 03 §M defaults (the SeedRunner inserts them on start).
internal sealed class StoreSettingsProvider(IServiceScopeFactory scopes, IClock clock) : IStoreSettings, ISettingsProvider, IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (StoreSettings Value, Dictionary<string, JsonElement> Raw, DateTimeOffset LoadedAt)? _cache;

    public async Task<StoreSettings> GetAsync(CancellationToken ct = default) => (await LoadAsync(ct)).Value;

    public async Task<T> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var raw = (await LoadAsync(ct)).Raw;
        return raw.TryGetValue(key, out var element) ? element.Deserialize<T>()! : default!;
    }

    public void Invalidate() => _cache = null;

    public void Dispose() => _gate.Dispose();

    private async Task<(StoreSettings Value, Dictionary<string, JsonElement> Raw, DateTimeOffset LoadedAt)> LoadAsync(CancellationToken ct)
    {
        if (_cache is { } hit && clock.UtcNow - hit.LoadedAt < Lifetime)
        {
            return hit;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_cache is { } again && clock.UtcNow - again.LoadedAt < Lifetime)
            {
                return again;
            }

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
            var rows = await db.Settings.AsNoTracking().Select(s => new { s.Key, s.Value }).ToListAsync(ct);
            var raw = rows.ToDictionary(r => r.Key, r => JsonDocument.Parse(r.Value).RootElement.Clone(), StringComparer.Ordinal);
            var loaded = (Parse(raw), raw, clock.UtcNow);
            _cache = loaded;
            return loaded;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static StoreSettings Parse(IReadOnlyDictionary<string, JsonElement> raw)
    {
        bool Bool(string key, bool fallback) => raw.TryGetValue(key, out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : fallback;
        decimal Dec(string key, decimal fallback) => raw.TryGetValue(key, out var e) ? e.ValueKind switch
        {
            JsonValueKind.Number => e.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(e.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
            _ => fallback,
        } : fallback;
        int Int(string key, int fallback) => (int)Dec(key, fallback);
        string? Str(string key) => raw.TryGetValue(key, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

        List<DedicationTemplate> templates = [];
        if (raw.TryGetValue(SettingKeys.DedicationTemplates, out var list) && list.ValueKind == JsonValueKind.Array)
        {
            templates.AddRange(list.EnumerateArray().Select(t => new DedicationTemplate(
                t.GetProperty("key").GetString()!, t.GetProperty("ar").GetString()!, t.TryGetProperty("en", out var en) ? en.GetString() ?? string.Empty : string.Empty)));
        }

        return new StoreSettings(
            Bool(SettingKeys.VatEnabled, false),
            Dec(SettingKeys.VatRate, 15m),
            Str(SettingKeys.SellerVatNo),
            Money.Round(Dec(SettingKeys.ShippingFlatFee, 25m)),
            Money.Round(Dec(SettingKeys.MinGiftAmount, 10m)),
            Money.Round(Dec(SettingKeys.CartMaxTotal, 100_000m)),
            Int(SettingKeys.OrderPaymentTimeoutMin, 60),
            Int(SettingKeys.DownloadLimit, 5),
            Int(SettingKeys.AllocationAlertDays, 30),
            Int(SettingKeys.AllocationEscalateDays, 90),
            Int(SettingKeys.LapseDays, 60),
            Int(SettingKeys.RefundWindowDays, 7),
            Int(SettingKeys.RefundProgressMaxPct, 20),
            Int(SettingKeys.LowStockDefaultThreshold, 20),
            Int(SettingKeys.GiftRefundWindowHours, 48),
            Bool(SettingKeys.DedicationFreeTextEnabled, false),
            templates);
    }
}
