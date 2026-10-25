using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.DTOs.Settings;

namespace Ta3lom.Store.Application.Features.Settings.Queries.ListSettings;

/// `GET /v1/admin/settings` .
public sealed record ListSettingsQuery : IQuery<IReadOnlyList<SettingResponse>>;

internal sealed class ListSettingsQueryHandler(IApplicationDbContext db) : IQueryHandler<ListSettingsQuery, IReadOnlyList<SettingResponse>>
{
    public async Task<Result<IReadOnlyList<SettingResponse>>> HandleAsync(ListSettingsQuery query, CancellationToken ct = default)
    {
        var rows = await db.Settings.AsNoTracking().OrderBy(s => s.Key).ToListAsync(ct);
        return rows.Select(s => new SettingResponse(s.Key, SettingRules.ToWire(s.Key, s.Value), s.UpdatedAt, s.UpdatedBy)).ToList();
    }
}
