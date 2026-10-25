namespace Ta3lom.Store.Application.Abstractions.Pagination;

/// A page of results. Serialised as `{ items, total, page, pageSize }`.
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// `?page=1&amp;pageSize=20`, 1-based, pageSize 1–100.
public sealed record PageRequest(int Page = 1, int PageSize = 20)
{
    public const int MaxPageSize = 100;

    public int Skip => (Math.Max(Page, 1) - 1) * Math.Clamp(PageSize, 1, MaxPageSize);

    public int Take => Math.Clamp(PageSize, 1, MaxPageSize);

    public int SafePage => Math.Max(Page, 1);
}
