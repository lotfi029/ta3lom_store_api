using Microsoft.EntityFrameworkCore;
using Ta3lom.Store.Application.Abstractions.Data;
using Ta3lom.Store.Application.Abstractions.Pagination;
using Ta3lom.Store.Application.DTOs.Media;

namespace Ta3lom.Store.Application.Features.Media.Queries.ListMedia;

/// `GET /v1/admin/media`: newest first, optional `kind`.
public sealed record ListMediaQuery(string? Kind, int Page = 1, int PageSize = 20) : IQuery<PagedResult<MediaAssetResponse>>;

internal sealed class ListMediaQueryHandler(IApplicationDbContext db, IFileUrls urls) : IQueryHandler<ListMediaQuery, PagedResult<MediaAssetResponse>>
{
    public async Task<Result<PagedResult<MediaAssetResponse>>> HandleAsync(ListMediaQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var assets = db.MediaAssets.AsNoTracking();
        if (EnumWire.TryParse<MediaKind>(query.Kind, out var kind))
        {
            assets = assets.Where(m => m.Kind == kind);
        }

        var total = await assets.CountAsync(ct);
        var rows = await assets.OrderByDescending(m => m.CreatedAt).Skip(page.Skip).Take(page.Take).ToListAsync(ct);
        return new PagedResult<MediaAssetResponse>([.. rows.Select(m => MediaMapping.ToResponse(m, urls))], total, page.SafePage, page.Take);
    }
}
