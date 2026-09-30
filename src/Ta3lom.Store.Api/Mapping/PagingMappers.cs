using Ta3lom.Store.Application.Abstractions;
using Ta3lom.Store.Contracts.Common;

namespace Ta3lom.Store.Api.Mapping;

/// Application results → HTTP contracts. Static mappers only (no AutoMapper); one file per module.
public static class PagingMappers
{
    public static PagedResponse<TResponse> ToResponse<TItem, TResponse>(this PagedResult<TItem> page, Func<TItem, TResponse> map)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(map);
        return new PagedResponse<TResponse>([.. page.Items.Select(map)], page.Total, page.Page, page.PageSize);
    }
}
